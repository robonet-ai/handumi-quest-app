using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

/// <summary>
/// Keeps the reconstructed project on the Android/OpenXR settings recovered
/// from the reference APK. The method is idempotent and can also be invoked
/// manually from the Tools menu.
/// </summary>
[InitializeOnLoad]
internal static class ConfigureHandUMIQuestProject
{
    private const string PackageIdentifier =
        "com.handumi.questapp";

    static ConfigureHandUMIQuestProject()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/HandUMI Quest/Apply Recovered Android Settings")]
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
            Debug.LogError("[HandUMI Quest] Could not load the Meta XR project configuration.");
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
        PlayerSettings.productName = "HandUMIQuestApp-v0.2.1";
        PlayerSettings.bundleVersion = "0.2.1";
        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android,
            PackageIdentifier);

        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.Android.bundleVersionCode = 3;
        // Unity issue UUM-139694 can deadlock GameActivity's native lifecycle
        // callbacks on Quest when the headset sleeps or loses focus. Unity's
        // Activity entry point uses the Java player loop and is not affected.
        // Keep this explicit: GameActivity is the default for new projects and
        // otherwise gets restored easily by project regeneration.
        PlayerSettings.Android.applicationEntry =
            AndroidApplicationEntry.Activity;
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
                "[HandUMI Quest] Could not initialize Android XR Plug-in Management settings.");
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

/// <summary>
/// Restores the lifecycle-safe Activity after Meta XR 74's Gradle callback
/// unconditionally replaces it with GameActivity on Unity 2023.2 and newer.
/// </summary>
internal sealed class QuestActivityLifecycleBuildProcessor :
    IPostGenerateGradleAndroidProject
{
    private const string AndroidNamespace =
        "http://schemas.android.com/apk/res/android";
    private const string ActivityClass =
        "com.unity3d.player.UnityPlayerActivity";
    private const string GameActivityClass =
        "com.unity3d.player.UnityPlayerGameActivity";

    // Meta XR 74's OVRGradleGeneration callback runs at 99999.
    public int callbackOrder => 100000;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifestPath =
            Path.Combine(path, "src", "main", "AndroidManifest.xml");
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(manifestPath);

        XmlNodeList activities =
            document.SelectNodes("/manifest/application/activity");
        bool changed = false;
        foreach (XmlNode node in activities)
        {
            if (!(node is XmlElement activity) ||
                activity.GetAttribute("name", AndroidNamespace) != GameActivityClass)
                continue;

            activity.SetAttribute("name", AndroidNamespace, ActivityClass);
            activity.SetAttribute(
                "theme", AndroidNamespace, "@style/UnityThemeSelector");

            for (int i = activity.ChildNodes.Count - 1; i >= 0; --i)
            {
                if (activity.ChildNodes[i] is XmlElement metadata &&
                    metadata.LocalName == "meta-data" &&
                    metadata.GetAttribute("name", AndroidNamespace) ==
                    "android.app.lib_name")
                {
                    activity.RemoveChild(metadata);
                }
            }

            changed = true;
        }

        if (!changed)
            return;

        document.Save(manifestPath);
        Debug.Log(
            "[HandUMI Quest] Restored UnityPlayerActivity after Meta XR manifest processing.");
    }
}
