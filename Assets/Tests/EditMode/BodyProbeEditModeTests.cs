using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BodyProbeEditModeTests
{
    [Test]
    public void PermissionControllerDisablesBodyWhilePausedAndRestoresInEditor()
    {
        GameObject owner = new GameObject("permission lifecycle test");
        try
        {
            OVRBody body = owner.AddComponent<OVRBody>();
            BodyProbePermissionController controller =
                owner.AddComponent<BodyProbePermissionController>();
            controller.Configure(body);
            InvokePrivate(controller, "Awake", null);
            InvokePrivate(controller, "Start", null);
            Assert.That(controller.PermissionState, Is.EqualTo("EditorGranted"));
            Assert.That(body.enabled, Is.True);

            InvokePrivate(controller, "OnApplicationPause", true);
            Assert.That(body.enabled, Is.False);
            InvokePrivate(controller, "OnApplicationPause", false);
            Assert.That(body.enabled, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    [TestCase(BodyProbeWireProtocol.UpperBodyJointCount, "UpperBody")]
    [TestCase(BodyProbeWireProtocol.FullBodyJointCount, "FullBody")]
    public void SerializesEveryBodyJointWithoutChangingFlags(
        int jointCount,
        string expectedJointSet)
    {
        OVRPlugin.BodyState state = SyntheticState(jointCount);
        var body = new BodyProbeBodyData();

        BodyProbeWireProtocol.PopulateBody(
            body,
            state,
            OVRPlugin.BodyJointSet.FullBody);

        Assert.That(body.active, Is.True);
        Assert.That(body.activeJointSet, Is.EqualTo(expectedJointSet));
        Assert.That(body.jointCount, Is.EqualTo(jointCount));
        Assert.That(body.joints, Has.Length.EqualTo(jointCount));
        Assert.That(body.jointNames, Has.Length.EqualTo(jointCount));
        Assert.That(body.jointLocationFlags, Has.Length.EqualTo(jointCount));
        Assert.That(body.jointPoses, Has.Length.EqualTo(jointCount * 7));
        for (int i = 0; i < jointCount; ++i)
        {
            Assert.That(body.joints[i].index, Is.EqualTo(i));
            Assert.That(body.joints[i].locationFlags, Is.EqualTo(i % 16));
            Assert.That(body.joints[i].position.x, Is.EqualTo(i + 0.25f));
            Assert.That(body.joints[i].position.z, Is.EqualTo(-(i + 0.75f)));
            Assert.That(body.joints[i].orientation.w, Is.EqualTo(1f));
        }

        string json = JsonUtility.ToJson(body);
        Assert.That(json, Does.Contain($"\"jointCount\":{jointCount}"));
        Assert.That(json, Does.Contain("\"jointLocationFlags\":[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15"));
        Assert.That(json, Does.Not.Contain("\"joints\":"));
        Assert.That(json, Does.Contain(expectedJointSet == "FullBody"
            ? "FullBody_RightFootBall"
            : "Body_RightHandLittleTip"));
    }

    private static void InvokePrivate(
        object target,
        string methodName,
        object argument)
    {
        System.Reflection.MethodInfo method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing private method {methodName}");
        method.Invoke(
            target,
            argument == null ? null : new[] { argument });
    }

    [Test]
    public void ConvertsNativeBodyPoseToControllerUnityTrackingSpace()
    {
        OVRPlugin.BodyState state = SyntheticState(BodyProbeWireProtocol.FullBodyJointCount);
        OVRPlugin.BodyJointLocation joint = state.JointLocations[0];
        joint.Pose = new OVRPlugin.Posef
        {
            Position = new OVRPlugin.Vector3f { x = 1f, y = 2f, z = 3f },
            Orientation = new OVRPlugin.Quatf { x = 0.1f, y = 0.2f, z = 0.3f, w = 0.9f }
        };
        state.JointLocations[0] = joint;
        var body = new BodyProbeBodyData();

        BodyProbeWireProtocol.PopulateBody(
            body,
            state,
            OVRPlugin.BodyJointSet.FullBody);

        Assert.That(body.joints[0].position.x, Is.EqualTo(1f));
        Assert.That(body.joints[0].position.y, Is.EqualTo(2f));
        Assert.That(body.joints[0].position.z, Is.EqualTo(-3f));
        Assert.That(body.joints[0].orientation.x, Is.EqualTo(-0.1f));
        Assert.That(body.joints[0].orientation.y, Is.EqualTo(-0.2f));
        Assert.That(body.joints[0].orientation.z, Is.EqualTo(0.3f));
        Assert.That(body.joints[0].orientation.w, Is.EqualTo(0.9f));
    }

    [Test]
    public void InactiveBodyClearsOldJointsInsteadOfReusingPoses()
    {
        var body = new BodyProbeBodyData();
        BodyProbeWireProtocol.PopulateBody(
            body,
            SyntheticState(BodyProbeWireProtocol.FullBodyJointCount),
            OVRPlugin.BodyJointSet.FullBody);
        Assert.That(body.joints, Has.Length.EqualTo(84));

        BodyProbeWireProtocol.PopulateBody(
            body,
            null,
            OVRPlugin.BodyJointSet.FullBody);

        Assert.That(body.active, Is.False);
        Assert.That(body.activeJointSet, Is.EqualTo("None"));
        Assert.That(body.jointCount, Is.Zero);
        Assert.That(body.joints, Is.Empty);
        Assert.That(body.jointNames, Is.Empty);
        Assert.That(body.jointLocationFlags, Is.Empty);
        Assert.That(body.jointPoses, Is.Empty);
        Assert.That(body.sourceTimeNs, Is.Zero);
    }

    [Test]
    public void ExtendedPoseKeepsLegacyFieldsFlatAndAdditive()
    {
        var pose = new BodyProbePoseData
        {
            seq = 17,
            hmdPosition = new Vector3(1f, 2f, 3f),
            leftTracked = true,
            ovrTimeNs = 123
        };
        BodyProbeWireProtocol.PopulateBody(
            pose.body,
            SyntheticState(BodyProbeWireProtocol.UpperBodyJointCount),
            OVRPlugin.BodyJointSet.UpperBody);

        string frame = Encoding.UTF8.GetString(BodyProbeWireProtocol.EncodePose(pose));
        Assert.That(frame, Does.EndWith("\n"));
        Assert.That(frame, Does.Contain("\"hmdPosition\":"));
        Assert.That(frame, Does.Contain("\"leftTracked\":true"));
        Assert.That(frame, Does.Contain("\"ovrTimeNs\":123"));
        Assert.That(frame, Does.Contain("\"packetType\":\"body_pose\""));
        Assert.That(frame, Does.Contain("\"schema\":\"tracking_packet_v2\""));
        Assert.That(frame, Does.Contain("\"sourceSchemaVersion\":2"));
        Assert.That(frame, Does.Contain("\"timestampQuality\":\"DIAGNOSTIC_ONLY\""));
        Assert.That(frame, Does.Contain("\"seq\":17"));
        Assert.That(frame, Does.Contain("\"body\":"));
        Assert.That(frame, Does.Contain("\"jointLocationFlags\":"));
        Assert.That(frame, Does.Contain("\"jointPoses\":"));
        Assert.That(frame, Does.Not.Contain("\"joints\":"));
        Assert.That(frame.IndexOf("\"hmdPosition\":", StringComparison.Ordinal),
            Is.LessThan(frame.IndexOf("\"body\":", StringComparison.Ordinal)));
    }

    [Test]
    public void SessionManifestIsASeparateFirstClassRecord()
    {
        var manifest = new BodyProbeSessionManifest
        {
            sessionId = "test-session",
            firstPoseSeq = 0,
            sourceCommit = "abc123",
            requestedJointSet = "FullBody",
            requestedFidelity = "High"
        };
        string frame = Encoding.UTF8.GetString(
            BodyProbeWireProtocol.EncodeManifest(manifest));

        Assert.That(frame, Does.EndWith("\n"));
        Assert.That(frame, Does.Contain("\"packetType\":\"session_manifest\""));
        Assert.That(frame, Does.Contain("\"schema\":\"handumi_quest_body_probe_manifest_v1\""));
        Assert.That(frame, Does.Contain("\"firstPoseSeq\":0"));
        Assert.That(frame, Does.Not.Contain("\"hmdPosition\":"));
    }

    [Test]
    public void BodyProbeProfileHasIsolatedIdentityAndMetaSettings()
    {
        Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
            Is.EqualTo("com.handumi.questapp.bodyprobe"));
        Assert.That(PlayerSettings.productName,
            Is.EqualTo("HandUMI Body Probe"));
        Assert.That(OVRProjectConfig.CachedProjectConfig.bodyTrackingSupport,
            Is.EqualTo(OVRProjectConfig.FeatureSupport.Supported));
        Assert.That(OVRProjectConfig.CachedProjectConfig.insightPassthroughSupport,
            Is.EqualTo(OVRProjectConfig.FeatureSupport.Required));
        Assert.That(OVRProjectConfig.CachedProjectConfig.handTrackingSupport,
            Is.EqualTo(OVRProjectConfig.HandTrackingSupport.ControllersOnly));
        Assert.That(OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingJointSet,
            Is.EqualTo(OVRPlugin.BodyJointSet.FullBody));
        Assert.That(OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingFidelity,
            Is.EqualTo(OVRPlugin.BodyTrackingFidelity2.High));

        string manifest = File.ReadAllText("Assets/Plugins/Android/AndroidManifest.xml");
        Assert.That(manifest, Does.Contain(BodyProbePermissionController.PermissionId));
        Assert.That(manifest, Does.Contain("com.oculus.software.body_tracking"));
    }

    [Test]
    public void DiagnosticSceneIsSeparateAndUsesFullBodyStageTracking()
    {
        Assert.That(EditorBuildSettings.scenes, Has.Length.EqualTo(1));
        Assert.That(EditorBuildSettings.scenes[0].path,
            Is.EqualTo("Assets/HandUMIBodyProbe/Scenes/HandUMIBodyProbe.unity"));
        Scene scene = EditorSceneManager.OpenScene(
            "Assets/HandUMIBodyProbe/Scenes/HandUMIBodyProbe.unity",
            OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();
        OVRManager manager = roots.Select(root => root.GetComponent<OVRManager>())
            .FirstOrDefault(value => value != null);
        OVRCameraRig rig = roots.Select(root => root.GetComponent<OVRCameraRig>())
            .FirstOrDefault(value => value != null);
        OVRPassthroughLayer passthrough = roots
            .SelectMany(root => root.GetComponentsInChildren<OVRPassthroughLayer>(true))
            .FirstOrDefault();
        OVRBody body = roots.SelectMany(root => root.GetComponentsInChildren<OVRBody>(true))
            .FirstOrDefault();
        BodyProbeSender sender = roots
            .SelectMany(root => root.GetComponentsInChildren<BodyProbeSender>(true))
            .FirstOrDefault();
        BodyProbeStatusDisplay status = roots
            .SelectMany(root => root.GetComponentsInChildren<BodyProbeStatusDisplay>(true))
            .FirstOrDefault();

        Assert.That(manager, Is.Not.Null);
        Assert.That(rig, Is.Not.Null);
        Assert.That(manager.trackingOriginType, Is.EqualTo(OVRManager.TrackingOrigin.Stage));
        Assert.That(manager.isInsightPassthroughEnabled, Is.True);
        Assert.That(manager.SimultaneousHandsAndControllersEnabled, Is.False);
        Assert.That(manager.launchSimultaneousHandsControllersOnStartup, Is.False);
        Assert.That(passthrough, Is.Not.Null);
        Assert.That(passthrough.overlayType,
            Is.EqualTo(OVROverlay.OverlayType.Underlay));
        Assert.That(passthrough.hidden, Is.False);
        Camera[] cameras = rig.GetComponentsInChildren<Camera>(true);
        Assert.That(cameras, Is.Not.Empty);
        Assert.That(cameras.All(camera =>
            camera.clearFlags == CameraClearFlags.SolidColor), Is.True);
        Assert.That(cameras.All(camera =>
            Mathf.Approximately(camera.backgroundColor.a, 0f)), Is.True);
        Assert.That(body, Is.Not.Null);
        Assert.That(body.ProvidedSkeletonType, Is.EqualTo(OVRPlugin.BodyJointSet.FullBody));
        Assert.That(sender, Is.Not.Null);
        Assert.That(status, Is.Not.Null);
        Assert.That(status.Sender, Is.SameAs(sender));
        Assert.That(status.StatusText, Is.Not.Null);
        Assert.That(status.StatusText.material, Is.Not.Null);
        Assert.That(status.StatusText.material.shader.name, Is.EqualTo("UI/Default"));
    }

    private static OVRPlugin.BodyState SyntheticState(int jointCount)
    {
        var joints = new OVRPlugin.BodyJointLocation[jointCount];
        for (int i = 0; i < joints.Length; ++i)
        {
            joints[i] = new OVRPlugin.BodyJointLocation
            {
                LocationFlags = (OVRPlugin.SpaceLocationFlags)(ulong)(i % 16),
                Pose = new OVRPlugin.Posef
                {
                    Position = new OVRPlugin.Vector3f
                    {
                        x = i + 0.25f,
                        y = i + 0.5f,
                        z = i + 0.75f
                    },
                    Orientation = OVRPlugin.Quatf.identity
                }
            };
        }
        return new OVRPlugin.BodyState
        {
            JointLocations = joints,
            Confidence = 0.75f,
            SkeletonChangedCount = 9,
            Time = 123.456789,
            CalibrationStatus = OVRPlugin.BodyTrackingCalibrationState.Valid,
            Fidelity = OVRPlugin.BodyTrackingFidelity2.High
        };
    }
}
