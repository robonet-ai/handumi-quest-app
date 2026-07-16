using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

[InitializeOnLoad]
public static class ConfigureHandUMIBodyProbeProject
{
    public const string PackageIdentifier = "com.handumi.questapp.bodyprobe";
    public const string ProductName = "HandUMI Body Probe";
    public const string VersionName = "0.1.2";

    static ConfigureHandUMIBodyProbeProject()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/HandUMI Body Probe/Apply Android and Meta Settings")]
    internal static void Apply()
    {
        // Reuse the proven Android/OpenXR/Activity/toolchain configuration,
        // then replace every setting that belongs to the isolated body probe.
        ConfigureHandUMIQuestProject.Apply();

        PlayerSettings.productName = ProductName;
        PlayerSettings.bundleVersion = VersionName;
        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android,
            PackageIdentifier);
        PlayerSettings.Android.bundleVersionCode = 3;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig == null)
            throw new System.InvalidOperationException(
                "Meta XR project configuration is unavailable.");
        projectConfig.handTrackingSupport =
            OVRProjectConfig.HandTrackingSupport.ControllersOnly;
        projectConfig.renderModelSupport =
            OVRProjectConfig.RenderModelSupport.Disabled;
        projectConfig.bodyTrackingSupport =
            OVRProjectConfig.FeatureSupport.Supported;
        projectConfig.insightPassthroughSupport =
            OVRProjectConfig.FeatureSupport.Required;
        OVRProjectConfig.CommitProjectConfig(projectConfig);

        OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
        runtimeSettings.BodyTrackingJointSet = OVRPlugin.BodyJointSet.FullBody;
        runtimeSettings.BodyTrackingFidelity = OVRPlugin.BodyTrackingFidelity2.High;
        OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);

        AssetDatabase.SaveAssets();
    }
}
