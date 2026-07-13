// Reconstructed from the reference APK.
// NOT the original AIRoA source code.
//
// CONFIRMED: source path was Assets/PoseSender.cs; class/type names, fields,
// ports, JSON keys, log strings, and IL2CPP-visible method names.
// STRONGLY INFERRED: the method bodies below reproduce the native behavior.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

[Serializable]
public sealed class PoseData
{
    public Vector3 hmdPosition;
    public Quaternion hmdRotation;

    public Vector3 leftControllerPosition;
    public Quaternion leftControllerRotation;
    public Vector3 rightControllerPosition;
    public Quaternion rightControllerRotation;

    public bool leftTriggerPressed;
    public bool rightTriggerPressed;
    public bool leftGripPressed;
    public bool rightGripPressed;

    public bool buttonAPressed;
    public bool buttonBPressed;
    public bool buttonXPressed;
    public bool buttonYPressed;

    public Vector2 leftJoystick;
    public Vector2 rightJoystick;

    public bool leftThumbstickClick;
    public bool rightThumbstickClick;
    public bool leftThumbstickTouched;
    public bool rightThumbstickTouched;

    public bool buttonATouched;
    public bool buttonBTouched;
    public bool buttonXTouched;
    public bool buttonYTouched;

    public bool leftIndexTriggerTouched;
    public bool rightIndexTriggerTouched;

    public long ovrTimeNs;
    public long unityTimeNs;
    public float deltaTime;

    public bool leftTracked;
    public bool rightTracked;
    public bool leftValid;
    public bool rightValid;

    public byte leftBattPct;
    public byte rightBattPct;
    public byte hmdBattPct;
    public bool hmdCharging;

    public bool startPressed;
    public bool backPressed;
}

public static class BatteryUtils
{
    // CONFIRMED signature and byte return type. The public Meta XR API name is
    // present in the same build and is the closest source-level equivalent.
    public static byte GetControllerBattery(OVRInput.Controller which)
    {
        return OVRInput.GetControllerBatteryPercentRemaining(which);
    }

    // Native body: SystemInfo.batteryLevel * 100, rounded/clamped to 0..100;
    // 255 is used when Unity reports an unavailable level (< 0).
    public static byte GetHmdBatteryPercent()
    {
        float level = SystemInfo.batteryLevel;
        if (level < 0f)
            return byte.MaxValue;

        return (byte)Mathf.Clamp(Mathf.RoundToInt(level * 100f), 0, 100);
    }

    // Native body treats Charging and Full as externally powered.
    public static bool IsHmdOnExternalPower()
    {
        BatteryStatus status = SystemInfo.batteryStatus;
        return status == BatteryStatus.Charging || status == BatteryStatus.Full;
    }
}

public sealed class PoseSender : MonoBehaviour
{
    [SerializeField] private int serverPort = 65432;
    [SerializeField] private OVRCameraRig ovrCameraRig;
    [SerializeField] private TMP_Text vrText;

    private TcpListener server;
    private readonly List<TcpClient> clients = new List<TcpClient>();
    private Thread listenThread;
    private volatile bool isRunning;

    private void Start()
    {
        StartServer();
    }

    private void StartServer()
    {
        string localIp = GetLocalIPAddress();
        server = new TcpListener(IPAddress.Any, serverPort);
        server.Start();
        isRunning = true;

        listenThread = new Thread(() =>
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = server.AcceptTcpClient();
                    lock (clients)
                        clients.Add(client);
                    Debug.Log("Client connected");
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
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        })
        {
            IsBackground = true,
            Name = "HandUMIQuestApp Pose TCP accept"
        };
        listenThread.Start();

        string status = $"Server running on {localIp}:{serverPort}";
        if (vrText != null)
            vrText.text = status;

        Debug.Log($"Pose server started on {localIp}:{serverPort}");
        Debug.Log(status);
    }

    private void Update()
    {
        if (!isRunning || ovrCameraRig == null || ovrCameraRig.centerEyeAnchor == null)
            return;

        OVRInput.Controller left = OVRInput.Controller.LTouch;
        OVRInput.Controller right = OVRInput.Controller.RTouch;

        PoseData data = new PoseData
        {
            hmdPosition = ovrCameraRig.centerEyeAnchor.localPosition,
            hmdRotation = ovrCameraRig.centerEyeAnchor.localRotation,

            leftControllerPosition = OVRInput.GetLocalControllerPosition(left),
            leftControllerRotation = OVRInput.GetLocalControllerRotation(left),
            rightControllerPosition = OVRInput.GetLocalControllerPosition(right),
            rightControllerRotation = OVRInput.GetLocalControllerRotation(right),

            leftTriggerPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, left),
            rightTriggerPressed = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, right),
            leftGripPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, left),
            rightGripPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, right),

            buttonAPressed = OVRInput.Get(OVRInput.RawButton.A, right),
            buttonBPressed = OVRInput.Get(OVRInput.RawButton.B, right),
            buttonXPressed = OVRInput.Get(OVRInput.RawButton.X, left),
            buttonYPressed = OVRInput.Get(OVRInput.RawButton.Y, left),

            leftJoystick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, left),
            rightJoystick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, right),

            leftThumbstickClick = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, left),
            rightThumbstickClick = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, right),
            leftThumbstickTouched = OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, left),
            rightThumbstickTouched = OVRInput.Get(OVRInput.Touch.PrimaryThumbstick, right),

            buttonATouched = OVRInput.Get(OVRInput.RawTouch.A, right),
            buttonBTouched = OVRInput.Get(OVRInput.RawTouch.B, right),
            buttonXTouched = OVRInput.Get(OVRInput.RawTouch.X, left),
            buttonYTouched = OVRInput.Get(OVRInput.RawTouch.Y, left),

            leftIndexTriggerTouched = OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, left),
            rightIndexTriggerTouched = OVRInput.Get(OVRInput.Touch.PrimaryIndexTrigger, right),

            // The APK multiplies the Oculus runtime clock and Unity monotonic
            // clock by 1e9 and serializes signed 64-bit values.
            ovrTimeNs = (long)(OVRPlugin.GetTimeInSeconds() * 1_000_000_000.0),
            unityTimeNs = (long)(Time.realtimeSinceStartupAsDouble * 1_000_000_000.0),
            deltaTime = Time.deltaTime,

            leftTracked = OVRInput.GetControllerPositionTracked(left),
            rightTracked = OVRInput.GetControllerPositionTracked(right),
            leftValid = OVRInput.GetControllerPositionValid(left),
            rightValid = OVRInput.GetControllerPositionValid(right),

            leftBattPct = BatteryUtils.GetControllerBattery(left),
            rightBattPct = BatteryUtils.GetControllerBattery(right),

            // The optimized Update body uses clamped/truncated Unity battery
            // level directly. This intentionally differs from the unused
            // 255-sentinel helper above.
            hmdBattPct = (byte)(Mathf.Clamp01(SystemInfo.batteryLevel) * 100f),
            hmdCharging = SystemInfo.batteryStatus == BatteryStatus.Charging ||
                          SystemInfo.batteryStatus == BatteryStatus.Full,

            startPressed = OVRInput.Get(OVRInput.RawButton.Start, OVRInput.Controller.Active),
            backPressed = OVRInput.Get(OVRInput.RawButton.Back, OVRInput.Controller.Active)
        };

        byte[] frame = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data) + "\n");

        lock (clients)
        {
            for (int i = clients.Count - 1; i >= 0; --i)
            {
                TcpClient client = clients[i];
                try
                {
                    if (client == null || !client.Connected)
                        throw new SocketException();

                    NetworkStream stream = client.GetStream();
                    stream.Write(frame, 0, frame.Length);
                }
                catch
                {
                    clients.RemoveAt(i);
                    try { client?.Close(); } catch { /* best effort */ }
                }
            }
        }
    }

    private void OnApplicationQuit()
    {
        isRunning = false;

        try { server?.Stop(); } catch { /* best effort */ }

        lock (clients)
        {
            foreach (TcpClient client in clients.Where(c => c != null))
            {
                try { client.Close(); } catch { /* best effort */ }
            }
            clients.Clear();
        }

        try
        {
            if (listenThread != null && listenThread.IsAlive)
                listenThread.Join(250);
        }
        catch { /* best effort */ }

        Debug.Log("Pose server stopped");
    }

    private string GetLocalIPAddress()
    {
        string hostName = Dns.GetHostName();
        foreach (IPAddress address in Dns.GetHostEntry(hostName).AddressList)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return address.ToString();
        }
        return hostName;
    }
}
