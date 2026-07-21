using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.OpenXR;

[DefaultExecutionOrder(100)]
public sealed class BodyProbeSender : MonoBehaviour
{
    private sealed class ClientState
    {
        public TcpClient Client;
        public bool ManifestSent;
    }

    [SerializeField] private int serverPort = HandUMIWireProtocol.PosePort;
    [SerializeField] private OVRCameraRig ovrCameraRig;
    [SerializeField] private OVRBody body;
    [SerializeField] private BodyProbePermissionController permissionController;

    private readonly List<ClientState> clients = new List<ClientState>();
    private readonly BodyProbePoseData packet = new BodyProbePoseData();
    private TcpListener server;
    private Thread listenThread;
    private volatile bool isRunning;
    private byte[] poseBuffer = new byte[64 * 1024];
    private string localIpAddress = IPAddress.Loopback.ToString();
    private float nextIpRefreshTime;
    private string sessionId;
    private string installedApkSha256 = "unavailable";
    private BodyProbeBuildInfoAsset buildInfo = new BodyProbeBuildInfoAsset();
    private long nextSequence;
    private long lastSequence = -1;
    private long nextBodyObservationSequence;
    private long lastBodySourceTimeNs = long.MinValue;
    private long lastSkeletonRevision = long.MinValue;
    private bool lastBodyActive;
    private bool hasBodyObservation;
    private bool componentStarted;
    private bool applicationPaused;
    private bool applicationFocused = true;

    public string LocalIpAddress => localIpAddress;
    public int ServerPort => serverPort;
    public int ConnectedClientCount
    {
        get
        {
            lock (clients)
                return clients.Count;
        }
    }
    public bool HasConnectedClient => ConnectedClientCount > 0;
    public bool BodyActive => packet.body.active;
    public string RequestedJointSet => packet.body.requestedJointSet ?? "FullBody";
    public string ActiveJointSet => packet.body.activeJointSet ?? "None";
    public int JointCount => packet.body.jointCount;
    public string CalibrationState => packet.body.calibrationState ?? "Invalid";
    public string Fidelity => packet.body.fidelity ?? "Unknown";
    public long LastSequence => lastSequence;
    public string PermissionState => permissionController != null
        ? permissionController.PermissionState
        : "Unavailable";

    public void Configure(
        OVRCameraRig cameraRig,
        OVRBody bodyComponent,
        BodyProbePermissionController permissions)
    {
        ovrCameraRig = cameraRig;
        body = bodyComponent;
        permissionController = permissions;
    }

    private void Start()
    {
        sessionId = Guid.NewGuid().ToString("N");
        LoadBuildInfo();
        installedApkSha256 = ComputeInstalledApkSha256();
        componentStarted = true;
        ApplyLifecycleState();
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        ApplyLifecycleState();
    }

    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        ApplyLifecycleState();
    }

    private void ApplyLifecycleState()
    {
        if (componentStarted && !applicationPaused && applicationFocused)
            StartServer();
        else
            StopServer();
    }

    private void Update()
    {
        if (!isRunning)
            return;

        if (Time.unscaledTime >= nextIpRefreshTime)
        {
            localIpAddress = GetLocalIPAddress();
            nextIpRefreshTime = Time.unscaledTime + 5f;
        }

        PruneDisconnectedClients();
        // Keep the in-headset diagnostic current even before a workstation
        // connects. Previously the status canvas showed the packet defaults
        // (inactive / no active joint set / zero joints) until the first TCP
        // client, which looked like a permission or runtime failure.
        OVRPlugin.BodyJointSet requestedJointSet =
            OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingJointSet;
        OVRPlugin.BodyState? state = body != null && body.enabled
            ? body.BodyState
            : null;
        BodyProbeWireProtocol.PopulateBody(packet.body, state, requestedJointSet);

        if (!HasConnectedClient)
            return;

        BodyProbeLegacySampler.Populate(packet, ovrCameraRig);
        bool isNewBodyObservation = !hasBodyObservation ||
                                    packet.body.active != lastBodyActive ||
                                    packet.body.sourceTimeNs != lastBodySourceTimeNs ||
                                    packet.body.skeletonRevision != lastSkeletonRevision;
        packet.body.isNewObservation = isNewBodyObservation;
        if (isNewBodyObservation)
        {
            packet.body.observationSeq = nextBodyObservationSequence++;
            lastBodyActive = packet.body.active;
            lastBodySourceTimeNs = packet.body.sourceTimeNs;
            lastSkeletonRevision = packet.body.skeletonRevision;
            hasBodyObservation = true;
        }
        packet.packetType = BodyProbeWireProtocol.PosePacketType;
        packet.seq = nextSequence++;
        lastSequence = packet.seq;
        int poseLength = EncodePoseIntoReusableBuffer(packet);

        lock (clients)
        {
            for (int i = clients.Count - 1; i >= 0; --i)
            {
                ClientState stateForClient = clients[i];
                try
                {
                    TcpClient client = stateForClient.Client;
                    if (client == null || !client.Connected)
                        throw new SocketException();

                    NetworkStream stream = client.GetStream();
                    if (!stateForClient.ManifestSent)
                    {
                        byte[] manifest = BodyProbeWireProtocol.EncodeManifest(CreateManifest());
                        stream.Write(manifest, 0, manifest.Length);
                        stateForClient.ManifestSent = true;
                    }
                    stream.Write(poseBuffer, 0, poseLength);
                }
                catch
                {
                    clients.RemoveAt(i);
                    try { stateForClient.Client?.Close(); } catch { }
                }
            }
        }
    }

    private void StartServer()
    {
        if (isRunning)
            return;

        localIpAddress = GetLocalIPAddress();
        nextIpRefreshTime = Time.unscaledTime + 5f;
        var listener = new TcpListener(IPAddress.Any, serverPort);
        listener.Start();
        server = listener;
        isRunning = true;
        listenThread = new Thread(() => AcceptClients(listener))
        {
            IsBackground = true,
            Name = "HandUMI Body Probe TCP accept"
        };
        listenThread.Start();
        Debug.Log($"[BodyProbe] Pose server started on {localIpAddress}:{serverPort}");
    }

    private void AcceptClients(TcpListener listener)
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                client.NoDelay = true;
                client.SendTimeout = 1000;
                lock (clients)
                    clients.Add(new ClientState { Client = client });
                Debug.Log("[BodyProbe] Client connected; manifest pending");
            }
            catch (SocketException)
            {
                if (!isRunning)
                    break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    private BodyProbeSessionManifest CreateManifest()
    {
        OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
        return new BodyProbeSessionManifest
        {
            sessionId = sessionId,
            firstPoseSeq = 0,
            buildId = buildInfo.buildId ?? "unavailable",
            sourceCommit = buildInfo.sourceCommit ?? "unavailable",
            artifactName = buildInfo.artifactName ?? "handumi-body-probe.apk",
            installedApkSha256 = installedApkSha256,
            productName = Application.productName,
            packageIdentifier = string.IsNullOrEmpty(Application.identifier)
                ? buildInfo.packageIdentifier
                : Application.identifier,
            versionName = Application.version,
            unityVersion = Application.unityVersion,
            metaXrCoreVersion = buildInfo.metaXrCoreVersion ?? "unavailable",
            metaXrInteractionOvrVersion = buildInfo.metaXrInteractionOvrVersion ?? "unavailable",
            openXrPluginVersion = buildInfo.openXrPluginVersion ?? "unavailable",
            questModel = SystemInfo.deviceModel,
            questOs = SystemInfo.operatingSystem,
            ovrPluginVersion = SafeValue(() => OVRPlugin.version.ToString()),
            ovrNativeSdkVersion = SafeValue(() => OVRPlugin.nativeSDKVersion.ToString()),
            nativeXrApi = SafeValue(() => OVRPlugin.nativeXrApi.ToString()),
            openXrRuntimeName = SafeValue(() => OpenXRRuntime.name),
            openXrRuntimeVersion = SafeValue(() => OpenXRRuntime.version),
            openXrApiVersion = SafeValue(() => OpenXRRuntime.apiVersion),
            openXrAvailableExtensions = SafeArray(OpenXRRuntime.GetAvailableExtensions),
            openXrEnabledExtensions = SafeArray(OpenXRRuntime.GetEnabledExtensions),
            bodyPermissionState = PermissionState,
            bodyTrackingSupported = SafeBool(() => OVRPlugin.bodyTrackingSupported),
            bodyTrackingEnabled = SafeBool(() => OVRPlugin.bodyTrackingEnabled),
            requestedJointSet = runtimeSettings.BodyTrackingJointSet.ToString(),
            activeJointSet = ActiveJointSet,
            requestedFidelity = runtimeSettings.BodyTrackingFidelity.ToString(),
            activeFidelity = Fidelity,
            calibrationSupport = "OVRPlugin 1.92 API exposed; runtime result requires device capture",
            calibrationState = CalibrationState,
            acquisitionSpace = "OpenXR Stage (floor)",
            sourceTimeMethod =
                "OVRPlugin.BodyState.Time seconds copied unchanged and rounded to sourceTimeNs; raw XrTime is not exposed by Meta XR 74 C#",
            sourceTimeDomain = BodyProbeWireProtocol.BodySourceTimeDomain,
            platformPoseClassification = BodyProbeWireProtocol.PoseClassification,
            limitation =
                "Unity/OVRPlugin body poses are platform estimates; this probe does not emit anatomical CoM"
        };
    }

    private int EncodePoseIntoReusableBuffer(BodyProbePoseData value)
    {
        string json = JsonUtility.ToJson(value);
        int required = Encoding.UTF8.GetByteCount(json) + 1;
        if (poseBuffer.Length < required)
            Array.Resize(ref poseBuffer, Math.Max(required, poseBuffer.Length * 2));
        int written = Encoding.UTF8.GetBytes(json, 0, json.Length, poseBuffer, 0);
        poseBuffer[written] = (byte)'\n';
        return written + 1;
    }

    private void LoadBuildInfo()
    {
        TextAsset asset = Resources.Load<TextAsset>("HandUMIBodyProbeBuildInfo");
        if (asset == null)
            return;
        try
        {
            BodyProbeBuildInfoAsset loaded =
                JsonUtility.FromJson<BodyProbeBuildInfoAsset>(asset.text);
            if (loaded != null)
                buildInfo = loaded;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[BodyProbe] Could not parse build info: {exception.Message}");
        }
    }

    private static string ComputeInstalledApkSha256()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (FileStream stream = File.OpenRead(Application.dataPath))
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[BodyProbe] Installed APK hash unavailable: {exception.Message}");
            return "unavailable";
        }
#else
        return "editor-not-an-apk";
#endif
    }

    private void PruneDisconnectedClients()
    {
        lock (clients)
        {
            for (int i = clients.Count - 1; i >= 0; --i)
            {
                TcpClient client = clients[i].Client;
                bool disconnected;
                try
                {
                    Socket socket = client?.Client;
                    disconnected = socket == null || !socket.Connected ||
                                   (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
                }
                catch
                {
                    disconnected = true;
                }

                if (!disconnected)
                    continue;
                clients.RemoveAt(i);
                try { client?.Close(); } catch { }
            }
        }
    }

    private void OnDestroy()
    {
        componentStarted = false;
        StopServer();
    }
    private void OnApplicationQuit() => StopServer();

    private void StopServer()
    {
        isRunning = false;
        TcpListener listener = server;
        server = null;
        try { listener?.Stop(); } catch { }
        lock (clients)
        {
            foreach (ClientState client in clients)
                try { client.Client?.Close(); } catch { }
            clients.Clear();
        }
        try
        {
            if (listenThread != null && listenThread.IsAlive &&
                Thread.CurrentThread != listenThread)
                listenThread.Join(1000);
        }
        catch { }
        listenThread = null;
    }

    private static string GetLocalIPAddress()
    {
        try
        {
            IPAddress loopback = null;
            foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (!IPAddress.IsLoopback(address))
                    return address.ToString();
                loopback = address;
            }
            return (loopback ?? IPAddress.Loopback).ToString();
        }
        catch (SocketException)
        {
            return IPAddress.Loopback.ToString();
        }
    }

    private static string SafeValue(Func<string> getter)
    {
        try { return getter() ?? ""; }
        catch { return "unavailable"; }
    }

    private static string[] SafeArray(Func<string[]> getter)
    {
        try { return getter() ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static bool SafeBool(Func<bool> getter)
    {
        try { return getter(); }
        catch { return false; }
    }
}

public static class BodyProbeLegacySampler
{
    public static void Populate(PoseData data, OVRCameraRig rig)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        OVRInput.Controller left = OVRInput.Controller.LTouch;
        OVRInput.Controller right = OVRInput.Controller.RTouch;
        data.hmdPosition = rig != null && rig.centerEyeAnchor != null
            ? rig.centerEyeAnchor.localPosition
            : Vector3.zero;
        data.hmdRotation = rig != null && rig.centerEyeAnchor != null
            ? rig.centerEyeAnchor.localRotation
            : Quaternion.identity;
        data.leftControllerPosition = OVRInput.GetLocalControllerPosition(left);
        data.leftControllerRotation = OVRInput.GetLocalControllerRotation(left);
        data.rightControllerPosition = OVRInput.GetLocalControllerPosition(right);
        data.rightControllerRotation = OVRInput.GetLocalControllerRotation(right);
        data.leftTriggerPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, left);
        data.rightTriggerPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, right);
        data.leftGripPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, left);
        data.rightGripPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, right);
        data.buttonAPressed = OVRInput.Get(OVRInput.RawButton.A, right);
        data.buttonBPressed = OVRInput.Get(OVRInput.RawButton.B, right);
        data.buttonXPressed = OVRInput.Get(OVRInput.RawButton.X, left);
        data.buttonYPressed = OVRInput.Get(OVRInput.RawButton.Y, left);
        data.leftJoystick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, left);
        data.rightJoystick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, right);
        data.leftThumbstickClick = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, left);
        data.rightThumbstickClick = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, right);
        data.leftThumbstickTouched = OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, left);
        data.rightThumbstickTouched = OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, right);
        data.buttonATouched = OVRInput.Get(OVRInput.RawTouch.A, right);
        data.buttonBTouched = OVRInput.Get(OVRInput.RawTouch.B, right);
        data.buttonXTouched = OVRInput.Get(OVRInput.RawTouch.X, left);
        data.buttonYTouched = OVRInput.Get(OVRInput.RawTouch.Y, left);
        data.leftIndexTriggerTouched = OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, left);
        data.rightIndexTriggerTouched = OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, right);
        data.ovrTimeNs = (long)(OVRPlugin.GetTimeInSeconds() * 1_000_000_000d);
        data.unityTimeNs = (long)(Time.realtimeSinceStartupAsDouble * 1_000_000_000d);
        data.deltaTime = Time.deltaTime;
        data.leftTracked = OVRInput.GetControllerPositionTracked(left);
        data.rightTracked = OVRInput.GetControllerPositionTracked(right);
        data.leftValid = OVRInput.GetControllerPositionValid(left);
        data.rightValid = OVRInput.GetControllerPositionValid(right);
        data.leftBattPct = BatteryUtils.GetControllerBattery(left);
        data.rightBattPct = BatteryUtils.GetControllerBattery(right);
        data.hmdBattPct = (byte)(Mathf.Clamp01(SystemInfo.batteryLevel) * 100f);
        data.hmdCharging = SystemInfo.batteryStatus == BatteryStatus.Charging ||
                           SystemInfo.batteryStatus == BatteryStatus.Full;
        data.startPressed = OVRInput.Get(OVRInput.RawButton.Start, OVRInput.Controller.Active);
        data.backPressed = OVRInput.Get(OVRInput.RawButton.Back, OVRInput.Controller.Active);
    }
}
