using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class HandUMIWireProtocolTests
{
    private static readonly string[] ExpectedPoseKeys =
    {
        "hmdPosition", "hmdRotation",
        "leftControllerPosition", "leftControllerRotation",
        "rightControllerPosition", "rightControllerRotation",
        "leftTriggerPressed", "rightTriggerPressed",
        "leftGripPressed", "rightGripPressed",
        "buttonAPressed", "buttonBPressed", "buttonXPressed", "buttonYPressed",
        "leftJoystick", "rightJoystick",
        "leftThumbstickClick", "rightThumbstickClick",
        "leftThumbstickTouched", "rightThumbstickTouched",
        "buttonATouched", "buttonBTouched", "buttonXTouched", "buttonYTouched",
        "leftIndexTriggerTouched", "rightIndexTriggerTouched",
        "ovrTimeNs", "unityTimeNs", "deltaTime",
        "leftTracked", "rightTracked", "leftValid", "rightValid",
        "leftBattPct", "rightBattPct", "hmdBattPct", "hmdCharging",
        "startPressed", "backPressed"
    };

    [Test]
    public void PoseDataFieldsMatchRecoveredLegacyContract()
    {
        string[] fields = typeof(PoseData)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => field.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(ExpectedPoseKeys, fields);
        Assert.That(fields, Has.Length.EqualTo(ExpectedPoseKeys.Length));
    }

    [Test]
    public void PoseFrameIsUtf8JsonWithExactlyOneTrailingNewline()
    {
        var pose = new PoseData
        {
            hmdPosition = new Vector3(1.25f, 2.5f, -3.75f),
            hmdRotation = Quaternion.identity,
            leftTracked = true,
            ovrTimeNs = 123456789L
        };

        byte[] encoded = HandUMIWireProtocol.EncodePoseFrame(pose);
        string frame = Encoding.UTF8.GetString(encoded);

        Assert.That(frame, Does.EndWith("\n"));
        Assert.That(frame, Does.Not.EndWith("\n\n"));
        Assert.That(frame.TrimEnd('\n'), Does.Contain("\"ovrTimeNs\":123456789"));
        Assert.That(frame.TrimEnd('\n'), Does.Contain("\"leftTracked\":true"));
        Assert.That(JsonUtility.FromJson<PoseData>(frame).hmdPosition,
            Is.EqualTo(pose.hmdPosition));
    }

    [Test]
    public void TimeSyncRequestAndResponseMatchLittleEndianLayout()
    {
        const long pcTime = 0x0102030405060708L;
        const long questTime = 0x1112131415161718L;
        byte[] request = new byte[HandUMIWireProtocol.TimeSyncRequestSize];
        request[0] = HandUMIWireProtocol.TimeSyncRequestId;
        Buffer.BlockCopy(BitConverter.GetBytes(pcTime), 0, request, 1, 8);

        Assert.That(HandUMIWireProtocol.TryParseTimeSyncRequest(request, out long parsed),
            Is.True);
        Assert.That(parsed, Is.EqualTo(pcTime));

        byte[] response = HandUMIWireProtocol.EncodeTimeSyncResponse(pcTime, questTime);
        CollectionAssert.AreEqual(
            new byte[]
            {
                2,
                8, 7, 6, 5, 4, 3, 2, 1,
                0x18, 0x17, 0x16, 0x15, 0x14, 0x13, 0x12, 0x11
            },
            response);
    }

    [TestCase(null)]
    [TestCase(new byte[0])]
    [TestCase(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void TimeSyncRejectsMalformedRequests(byte[] request)
    {
        Assert.That(HandUMIWireProtocol.TryParseTimeSyncRequest(request, out _), Is.False);
    }

    [Test]
    public void DefaultPortsMatchHandumiLegacyConfiguration()
    {
        Assert.That(HandUMIWireProtocol.PosePort, Is.EqualTo(65432));
        Assert.That(HandUMIWireProtocol.TimeSyncPort, Is.EqualTo(42000));
    }

    [Test]
    public void StatusTextShowsTheAppVersionAndEndpointForConnectedClients()
    {
        Assert.That(
            QuestStatusDisplay.FormatStatus(false, "192.168.1.20", 65432),
            Is.EqualTo("HandUMI Quest App (v0.2.1)"));
        Assert.That(
            QuestStatusDisplay.FormatStatus(true, "192.168.1.20", 65432),
            Is.EqualTo("HandUMI Quest App (v0.2.1)\nConnected • IP: 192.168.1.20:65432"));
    }

    [Test]
    public void MetaFeaturesSupportHandsAndRuntimeControllerModels()
    {
        OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
        Assert.That(config, Is.Not.Null);
        if (File.Exists("Assets/HandUMIBodyProbe/BodyProbeProject.marker"))
        {
            Assert.That(config.handTrackingSupport,
                Is.EqualTo(OVRProjectConfig.HandTrackingSupport.ControllersOnly));
            Assert.That(config.bodyTrackingSupport,
                Is.EqualTo(OVRProjectConfig.FeatureSupport.Supported));
            return;
        }
        Assert.That(config.handTrackingSupport,
            Is.EqualTo(OVRProjectConfig.HandTrackingSupport.ControllersAndHands));
        Assert.That(config.renderModelSupport,
            Is.EqualTo(OVRProjectConfig.RenderModelSupport.Enabled));
    }

    [Test]
    public void AndroidIdentitySupportsSideBySideInstall()
    {
        if (File.Exists("Assets/HandUMIBodyProbe/BodyProbeProject.marker"))
        {
            Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                Is.EqualTo("com.handumi.questapp.bodyprobe"));
            Assert.That(PlayerSettings.productName, Is.EqualTo("HandUMI Body Probe"));
            Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("0.1.1"));
            Assert.That(PlayerSettings.Android.bundleVersionCode, Is.EqualTo(2));
            Assert.That(PlayerSettings.Android.applicationEntry,
                Is.EqualTo(AndroidApplicationEntry.Activity));
            return;
        }
        Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
            Is.EqualTo("com.handumi.questapp"));
        Assert.That(PlayerSettings.productName,
            Is.EqualTo("HandUMIQuestApp-v0.2.1"));
        Assert.That(PlayerSettings.bundleVersion, Is.EqualTo("0.2.1"));
        Assert.That(PlayerSettings.Android.bundleVersionCode, Is.EqualTo(3));
        Assert.That(PlayerSettings.Android.applicationEntry,
            Is.EqualTo(AndroidApplicationEntry.Activity),
            "GameActivity can deadlock during Quest pause/resume (Unity UUM-139694).");
    }

    [Test]
    public void AndroidManifestUsesLifecycleSafeActivity()
    {
        string manifest = File.ReadAllText(
            "Assets/Plugins/Android/AndroidManifest.xml");

        Assert.That(manifest,
            Does.Contain("com.unity3d.player.UnityPlayerActivity"));
        Assert.That(manifest,
            Does.Not.Contain("com.unity3d.player.UnityPlayerGameActivity"),
            "The custom manifest must not override the lifecycle-safe Player setting.");
        Assert.That(manifest, Does.Contain("@style/UnityThemeSelector"));
        Assert.That(manifest, Does.Not.Contain("@style/BaseUnityGameActivityTheme"));
    }

    [Test]
    public void CompatibilitySceneIsEnabledAndWiresRecoveredServices()
    {
        const string scenePath =
            "Assets/HandUMIQuestApp/Scenes/HandUMIQuestCompatibility.unity";
        if (File.Exists("Assets/HandUMIBodyProbe/BodyProbeProject.marker"))
        {
            Assert.That(File.Exists(scenePath), Is.True,
                "The isolated body branch must retain the rollback scene source.");
            return;
        }
        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
        Assert.That(buildScenes, Has.Length.EqualTo(1));
        Assert.That(buildScenes[0].enabled, Is.True);
        Assert.That(buildScenes[0].path, Is.EqualTo(
            scenePath));

        Scene scene = EditorSceneManager.OpenScene(
            scenePath,
            OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();

        OVRManager manager = roots.Select(root => root.GetComponent<OVRManager>())
            .FirstOrDefault(component => component != null);
        OVRCameraRig rig = roots.Select(root => root.GetComponent<OVRCameraRig>())
            .FirstOrDefault(component => component != null);
        QuestStatusDisplay display = roots
            .SelectMany(root => root.GetComponentsInChildren<QuestStatusDisplay>(true))
            .FirstOrDefault();
        PoseSender sender = display != null ? display.PoseSender : null;

        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.trackingOriginType,
            Is.EqualTo(OVRManager.TrackingOrigin.Stage));
        Assert.That(rig, Is.Not.Null);
        Assert.That(sender, Is.Not.Null);
        Assert.That(display, Is.Not.Null);
        Assert.That(display.PoseSender, Is.SameAs(sender));
        Assert.That(display.StatusText, Is.Not.Null);
        Assert.That(display.transform.parent, Is.SameAs(rig.centerEyeAnchor));
        Assert.That(display.StatusText.text, Is.EqualTo(QuestStatusDisplay.AppTitle));
        Assert.That((Color32)display.StatusText.color,
            Is.EqualTo(QuestStatusDisplay.TextColor));
        Assert.That(roots.Any(root => root.GetComponent<TimeSyncServer>() != null),
            Is.True);
        Assert.That(roots.Any(root => root.GetComponent<QuestRefresh120>() != null),
            Is.True);
        Assert.That(roots.Any(root => root.GetComponent<SafeTrackingOrigin>() != null),
            Is.True);

        SerializedObject serializedSender = new SerializedObject(sender);
        Assert.That(
            serializedSender.FindProperty("ovrCameraRig").objectReferenceValue,
            Is.SameAs(rig));

        Camera[] cameras = rig.GetComponentsInChildren<Camera>(true);
        Assert.That(cameras, Is.Not.Empty);
        Assert.That(cameras.All(camera =>
            camera.clearFlags == CameraClearFlags.SolidColor), Is.True);
        Assert.That(cameras.All(camera =>
            ((Color32)camera.backgroundColor).Equals(
                QuestStatusDisplay.BackgroundColor)),
            Is.True);
        Assert.That(cameras.All(camera =>
            Mathf.Approximately(camera.nearClipPlane,
                QuestStatusDisplay.NearClipMeters)), Is.True);

        OVRHand[] hands = rig.GetComponentsInChildren<OVRHand>(true);
        Assert.That(hands, Has.Length.EqualTo(2));
        CollectionAssert.AreEquivalent(
            new[] { (int)OVRHand.Hand.HandLeft, (int)OVRHand.Hand.HandRight },
            hands.Select(hand => new SerializedObject(hand)
                .FindProperty("HandType").intValue));

        QuestControllerVisualSelector[] controllers =
            rig.GetComponentsInChildren<QuestControllerVisualSelector>(true);
        Assert.That(controllers, Has.Length.EqualTo(2));
        CollectionAssert.AreEquivalent(
            new[] { OVRInput.Controller.LTouch, OVRInput.Controller.RTouch },
            controllers.Select(controller => controller.Controller));
        Assert.That(controllers.All(controller =>
            controller.RuntimeVisual != null &&
            controller.PackagedFallbackVisual != null), Is.True);
        Assert.That(controllers.All(controller =>
            controller.RuntimeVisual.GetComponent<OVRRuntimeController>()
                .m_supportAnimation), Is.True);
        Assert.That(controllers.All(controller =>
            controller.RuntimeVisual.GetComponent<OVRRuntimeController>()
                .m_controllerModelShader != null &&
            controller.RuntimeVisual.GetComponent<OVRRuntimeController>()
                .m_controllerModelShader.name == "Meta/Lit"), Is.True);
        Assert.That(controllers.SelectMany(controller =>
                controller.PackagedFallbackVisual
                    .GetComponentsInChildren<Renderer>(true))
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .All(material => material.shader.name == "Meta/Lit"), Is.True);
        Assert.That(display.StatusText.material, Is.Not.Null);
        Assert.That(display.StatusText.material.shader.name, Is.EqualTo("UI/Default"));
    }
}
