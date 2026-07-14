using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class BodyProbeVector3
{
    public float x;
    public float y;
    public float z;
}

[Serializable]
public sealed class BodyProbeQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;
}

[Serializable]
public sealed class BodyProbeJoint
{
    public int index;
    public string name;
    public long locationFlags;
    public BodyProbeVector3 position = new BodyProbeVector3();
    public BodyProbeQuaternion orientation = new BodyProbeQuaternion();
}

[Serializable]
public sealed class BodyProbeBodyData
{
    public bool active;
    public string requestedJointSet;
    public string activeJointSet;
    public int jointCount;
    public float confidence;
    public string calibrationState;
    public string fidelity;
    public long skeletonRevision;
    public double sourceTimeSeconds;
    public long sourceTimeNs;
    public string sourceTimeDomain;
    public string poseClassification;
    public BodyProbeJoint[] joints = Array.Empty<BodyProbeJoint>();
}

/// <summary>
/// Additive body diagnostic record. Every inherited field is the unchanged
/// controller/HMD compatibility field at the top level of the JSON object.
/// </summary>
[Serializable]
public sealed class BodyProbePoseData : PoseData
{
    public string packetType = BodyProbeWireProtocol.PosePacketType;
    public long seq;
    public BodyProbeBodyData body = new BodyProbeBodyData();
}

[Serializable]
public sealed class BodyProbeSessionManifest
{
    public string packetType = BodyProbeWireProtocol.ManifestPacketType;
    public string schema = BodyProbeWireProtocol.ManifestSchema;
    public string sessionId;
    public long firstPoseSeq;
    public string buildId;
    public string sourceCommit;
    public string artifactName;
    public string installedApkSha256;
    public string productName;
    public string packageIdentifier;
    public string versionName;
    public string unityVersion;
    public string metaXrCoreVersion;
    public string metaXrInteractionOvrVersion;
    public string openXrPluginVersion;
    public string questModel;
    public string questOs;
    public string ovrPluginVersion;
    public string ovrNativeSdkVersion;
    public string nativeXrApi;
    public string openXrRuntimeName;
    public string openXrRuntimeVersion;
    public string openXrApiVersion;
    public string[] openXrAvailableExtensions = Array.Empty<string>();
    public string[] openXrEnabledExtensions = Array.Empty<string>();
    public string bodyPermissionState;
    public bool bodyTrackingSupported;
    public bool bodyTrackingEnabled;
    public string requestedJointSet;
    public string activeJointSet;
    public string requestedFidelity;
    public string activeFidelity;
    public string calibrationSupport;
    public string calibrationState;
    public string acquisitionSpace;
    public string sourceTimeMethod;
    public string sourceTimeDomain;
    public string platformPoseClassification;
    public string limitation;
}

[Serializable]
public sealed class BodyProbeBuildInfoAsset
{
    public string buildId;
    public string sourceCommit;
    public string artifactName;
    public string packageIdentifier;
    public string metaXrCoreVersion;
    public string metaXrInteractionOvrVersion;
    public string openXrPluginVersion;
}

public static class BodyProbeWireProtocol
{
    public const string ManifestPacketType = "session_manifest";
    public const string PosePacketType = "body_pose";
    public const string ManifestSchema = "handumi_quest_body_probe_manifest_v1";
    public const string PoseClassification = "PLATFORM_ESTIMATED";
    public const string BodySourceTimeDomain = "OVRPlugin.BodyState.Time.seconds";
    public const int UpperBodyJointCount = 70;
    public const int FullBodyJointCount = 84;

    private static readonly string[] UpperBodyNames = BuildJointNames(
        "Body_", UpperBodyJointCount);
    private static readonly string[] FullBodyNames = BuildJointNames(
        "FullBody_", FullBodyJointCount);

    public static byte[] EncodeManifest(BodyProbeSessionManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        return EncodeJsonLine(manifest);
    }

    public static byte[] EncodePose(BodyProbePoseData pose)
    {
        if (pose == null)
            throw new ArgumentNullException(nameof(pose));

        return EncodeJsonLine(pose);
    }

    public static void PopulateBody(
        BodyProbeBodyData target,
        OVRPlugin.BodyState? state,
        OVRPlugin.BodyJointSet requestedJointSet)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        target.requestedJointSet = requestedJointSet.ToString();
        target.sourceTimeDomain = BodySourceTimeDomain;
        target.poseClassification = PoseClassification;

        if (!state.HasValue || state.Value.JointLocations == null)
        {
            target.active = false;
            target.activeJointSet = "None";
            target.jointCount = 0;
            target.confidence = 0f;
            target.calibrationState = OVRPlugin.BodyTrackingCalibrationState.Invalid.ToString();
            target.fidelity = "Unknown";
            target.skeletonRevision = 0;
            target.sourceTimeSeconds = 0d;
            target.sourceTimeNs = 0;
            target.joints = Array.Empty<BodyProbeJoint>();
            return;
        }

        OVRPlugin.BodyState bodyState = state.Value;
        int jointCount = bodyState.JointLocations.Length;
        target.active = true;
        target.activeJointSet = JointSetNameForCount(jointCount);
        target.jointCount = jointCount;
        target.confidence = bodyState.Confidence;
        target.calibrationState = bodyState.CalibrationStatus.ToString();
        target.fidelity = bodyState.Fidelity.ToString();
        target.skeletonRevision = bodyState.SkeletonChangedCount;
        target.sourceTimeSeconds = bodyState.Time;
        target.sourceTimeNs = SecondsToNanoseconds(bodyState.Time);

        if (target.joints == null || target.joints.Length != jointCount)
            target.joints = CreateJointArray(jointCount);

        string[] names = jointCount == FullBodyJointCount
            ? FullBodyNames
            : jointCount == UpperBodyJointCount
                ? UpperBodyNames
                : null;

        for (int i = 0; i < jointCount; ++i)
        {
            OVRPlugin.BodyJointLocation source = bodyState.JointLocations[i];
            BodyProbeJoint destination = target.joints[i];
            destination.index = i;
            destination.name = names != null ? names[i] : $"Joint_{i}";
            destination.locationFlags = unchecked((long)(ulong)source.LocationFlags);
            destination.position.x = source.Pose.Position.x;
            destination.position.y = source.Pose.Position.y;
            destination.position.z = source.Pose.Position.z;
            destination.orientation.x = source.Pose.Orientation.x;
            destination.orientation.y = source.Pose.Orientation.y;
            destination.orientation.z = source.Pose.Orientation.z;
            destination.orientation.w = source.Pose.Orientation.w;
        }
    }

    public static string JointSetNameForCount(int jointCount)
    {
        if (jointCount == FullBodyJointCount)
            return OVRPlugin.BodyJointSet.FullBody.ToString();
        if (jointCount == UpperBodyJointCount)
            return OVRPlugin.BodyJointSet.UpperBody.ToString();
        return "Unknown";
    }

    public static long SecondsToNanoseconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d)
            return 0;

        double nanoseconds = seconds * 1_000_000_000d;
        if (nanoseconds >= long.MaxValue)
            return long.MaxValue;
        return (long)Math.Round(nanoseconds, MidpointRounding.AwayFromZero);
    }

    private static byte[] EncodeJsonLine(object value)
    {
        return Encoding.UTF8.GetBytes(JsonUtility.ToJson(value) + "\n");
    }

    private static BodyProbeJoint[] CreateJointArray(int count)
    {
        var joints = new BodyProbeJoint[count];
        for (int i = 0; i < joints.Length; ++i)
            joints[i] = new BodyProbeJoint { index = i };
        return joints;
    }

    private static string[] BuildJointNames(string prefix, int count)
    {
        var result = new string[count];
        foreach (string name in Enum.GetNames(typeof(OVRPlugin.BoneId))
                     .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (name.EndsWith("_Start", StringComparison.Ordinal) ||
                name.EndsWith("_End", StringComparison.Ordinal) ||
                name.EndsWith("_Invalid", StringComparison.Ordinal))
                continue;

            int index = (int)(OVRPlugin.BoneId)Enum.Parse(typeof(OVRPlugin.BoneId), name);
            if (index >= 0 && index < result.Length && result[index] == null)
                result[index] = name;
        }

        for (int i = 0; i < result.Length; ++i)
            result[i] ??= $"{prefix}Joint_{i}";
        return result;
    }
}
