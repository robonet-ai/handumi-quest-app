using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

/// <summary>
/// Keeps the reconstructed project on the Android/OpenXR settings recovered
/// from YubiQuestApp v0.1.0. The method is idempotent and can also be invoked
/// manually from the Tools menu.
/// </summary>
[InitializeOnLoad]
internal static class ConfigureYubiQuestProject
{
    private const string PackageIdentifier =
        "com.handumi.questapp";

    static ConfigureYubiQuestProject()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/YubiQuest/Apply Recovered Android Settings")]
    internal static void Apply()
    {
        ConfigurePlayer();
        ConfigureMetaFeatures();
        ConfigureOpenXR();
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureMetaFeatures()
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        if (projectConfig == null)
        {
            Debug.LogError("[YubiQuest] Could not load the Meta XR project configuration.");
            return;
        }

        projectConfig.handTrackingSupport =
            OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
        projectConfig.renderModelSupport =
            OVRProjectConfig.RenderModelSupport.Enabled;
        OVRProjectConfig.CommitProjectConfig(projectConfig);
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.productName = "HandUMIQuestApp-v0.1.0";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android,
            PackageIdentifier);

        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.Android.applicationEntry =
            AndroidApplicationEntry.GameActivity;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

#pragma warning disable CS0618
        PlayerSettings.SetScriptingBackend(
            BuildTargetGroup.Android,
            ScriptingImplementation.IL2CPP);
#pragma warning restore CS0618

        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.gpuSkinning = true;
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;

        // The recovered APK used Vulkan support and the performance metadata
        // reports GraphicsJobs=false. Keep that fidelity setting explicit.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.Android,
            new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.graphicsJobs = false;

        QualitySettings.pixelLightCount = 1;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
    }

    private static void ConfigureOpenXR()
    {
        const BuildTargetGroup target = BuildTargetGroup.Android;
        XRGeneralSettings generalSettings =
            XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(target);

        if (generalSettings == null)
        {
            EnsureFolder("Assets/XR");

            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget settingsPerTarget);

            if (settingsPerTarget == null)
            {
                settingsPerTarget =
                    ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(
                    settingsPerTarget,
                    "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    settingsPerTarget,
                    true);
            }

            if (!settingsPerTarget.HasManagerSettingsForBuildTarget(target))
                settingsPerTarget.CreateDefaultManagerSettingsForBuildTarget(target);

            generalSettings =
                XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(target);
        }

        if (generalSettings == null || generalSettings.Manager == null)
        {
            Debug.LogError(
                "[YubiQuest] Could not initialize Android XR Plug-in Management settings.");
            return;
        }

        OpenXRLoader loader = AssetDatabase.FindAssets("t:OpenXRLoader")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<OpenXRLoader>)
            .FirstOrDefault(item => item != null);

        if (loader == null)
        {
            EnsureFolder("Assets/XR/Loaders");
            loader = ScriptableObject.CreateInstance<OpenXRLoader>();
            AssetDatabase.CreateAsset(
                loader,
                "Assets/XR/Loaders/OpenXRLoader.asset");
        }

        if (!generalSettings.Manager.activeLoaders.Contains(loader))
            generalSettings.Manager.TryAddLoader(loader);

        OpenXRSettings openXRSettings =
            OpenXRSettings.GetSettingsForBuildTargetGroup(target);
        OculusTouchControllerProfile touchProfile =
            openXRSettings?.GetFeature<OculusTouchControllerProfile>();
        if (touchProfile != null)
        {
            touchProfile.enabled = true;
            EditorUtility.SetDirty(touchProfile);
        }

        EditorUtility.SetDirty(generalSettings.Manager);
        EditorUtility.SetDirty(generalSettings);
    }

    private static void EnsureFolder(string path)
    {
        string current = null;
        foreach (string segment in path.Split('/'))
        {
            string next = string.IsNullOrEmpty(current)
                ? segment
                : $"{current}/{segment}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segment);
            current = next;
        }
    }
}
