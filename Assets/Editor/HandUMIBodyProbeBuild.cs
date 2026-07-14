using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public static class HandUMIBodyProbeBuild
{
    public const string ScenePath =
        "Assets/HandUMIBodyProbe/Scenes/HandUMIBodyProbe.unity";
    public const string DefaultApkPath =
        "Builds/Android/handumi-body-probe.apk";
    public const string BaselineTag = "controller-baseline-v0.2.1";
    public const string BaselineCommit =
        "1597c240541510aca15d0030f35c3b9f17c8e4a2";
    public const string BaselineApkSha256 =
        "30683f102fd2b86ffb85770735985d4d0ca3b88e5c9ecceef5d7ea7068906dc9";

    private const string BuildInfoPath =
        "Assets/Resources/HandUMIBodyProbeBuildInfo.json";

    [MenuItem("Tools/HandUMI Body Probe/Rebuild Diagnostic Scene")]
    public static void RebuildScene()
    {
        ConfigureHandUMIBodyProbeProject.Apply();
        EnsureFolder("Assets/HandUMIBodyProbe/Scenes");

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "HandUMIBodyProbe";

        GameObject managerObject = new GameObject("OVRManager");
        OVRManager manager = managerObject.AddComponent<OVRManager>();
        manager.trackingOriginType = OVRManager.TrackingOrigin.Stage;
        manager.SimultaneousHandsAndControllersEnabled = false;
        manager.launchSimultaneousHandsControllersOnStartup = false;

        GameObject rigObject = new GameObject("OVRCameraRig");
        OVRCameraRig rig = rigObject.AddComponent<OVRCameraRig>();
        rig.EnsureGameObjectIntegrity();
        foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BodyProbeStatusDisplay.BackgroundColor;
            camera.nearClipPlane = QuestStatusDisplay.NearClipMeters;
        }

        GameObject services = new GameObject("HandUMIBodyProbeServices");
        OVRBody body = services.AddComponent<OVRBody>();
        body.ProvidedSkeletonType = OVRPlugin.BodyJointSet.FullBody;
        body.enabled = false;
        BodyProbePermissionController permission =
            services.AddComponent<BodyProbePermissionController>();
        permission.Configure(body);
        BodyProbeSender sender = services.AddComponent<BodyProbeSender>();
        sender.Configure(rig, body, permission);
        services.AddComponent<TimeSyncServer>();
        services.AddComponent<QuestRefresh120>();
        services.AddComponent<SafeTrackingOrigin>();

        CreateStatusCanvas(rig, sender);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException($"Could not save {ScenePath}");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log($"[BodyProbe] Rebuilt diagnostic scene: {ScenePath}");
    }

    [MenuItem("Tools/HandUMI Body Probe/Build Diagnostic APK")]
    public static void BuildAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException(
                "Android must be active; pass -buildTarget Android in batch mode.");

        ConfigureAndroidTools.Apply();
        ConfigureHandUMIBodyProbeProject.Apply();
        EnsureSceneIdentity();

        string outputVariable = Environment.GetEnvironmentVariable(
            "HANDUMI_BODY_PROBE_APK_OUTPUT");
        string outputPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(outputVariable)
                ? DefaultApkPath
                : outputVariable);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        string sourceCommit = ResolveSourceCommit();
        string buildId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "-" +
                         sourceCommit.Substring(0, Math.Min(12, sourceCommit.Length));
        WriteRuntimeBuildInfo(buildId, sourceCommit, Path.GetFileName(outputPath));

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException(
                $"Body probe build failed with {report.summary.totalErrors} errors.");

        string sha256 = ComputeSha256(outputPath);
        var manifest = new BodyProbeApkManifest
        {
            schema = "handumi_quest_body_probe_build_v1",
            buildId = buildId,
            sourceCommit = sourceCommit,
            sourceBranch = "feat/quest-body-diagnostic",
            artifact = Path.GetFileName(outputPath),
            artifactSha256 = sha256,
            artifactSizeBytes = (ulong)new FileInfo(outputPath).Length,
            baselineTag = BaselineTag,
            baselineCommit = BaselineCommit,
            baselineApkSha256 = BaselineApkSha256,
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            productName = PlayerSettings.productName,
            packageIdentifier = PlayerSettings.GetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android),
            versionName = PlayerSettings.bundleVersion,
            versionCode = PlayerSettings.Android.bundleVersionCode,
            scene = ScenePath,
            metaXrCore = PackageVersion("Packages/com.meta.xr.sdk.core"),
            metaXrInteractionOvr =
                PackageVersion("Packages/com.meta.xr.sdk.interaction.ovr"),
            openXr = PackageVersion("Packages/com.unity.xr.openxr"),
            requestedJointSet = OVRRuntimeSettings.GetRuntimeSettings()
                .BodyTrackingJointSet.ToString(),
            requestedFidelity = OVRRuntimeSettings.GetRuntimeSettings()
                .BodyTrackingFidelity.ToString(),
            acquisitionSpace = "OpenXR Stage",
            bodyPermission = BodyProbePermissionController.PermissionId,
            buildResult = report.summary.result.ToString()
        };
        File.WriteAllText(
            outputPath + ".manifest.json",
            JsonUtility.ToJson(manifest, true) + Environment.NewLine);
        File.WriteAllText(
            outputPath + ".sha256",
            $"{sha256}  {Path.GetFileName(outputPath)}{Environment.NewLine}");
        Debug.Log($"[BodyProbe] Built {outputPath} sha256={sha256}");
    }

    private static void CreateStatusCanvas(OVRCameraRig rig, BodyProbeSender sender)
    {
        GameObject canvasObject = new GameObject(
            "BodyProbeStatusCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(BodyProbeStatusDisplay));
        RectTransform canvasTransform = canvasObject.GetComponent<RectTransform>();
        canvasTransform.SetParent(rig.centerEyeAnchor, false);
        canvasTransform.localPosition = new Vector3(0f, -0.04f, 1.35f);
        canvasTransform.localRotation = Quaternion.identity;
        canvasTransform.localScale = Vector3.one * 0.0012f;
        canvasTransform.sizeDelta = new Vector2(1300f, 500f);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = rig.centerEyeAnchor.GetComponent<Camera>();

        GameObject textObject = new GameObject("StatusText", typeof(RectTransform));
        RectTransform textTransform = textObject.GetComponent<RectTransform>();
        textTransform.SetParent(canvasTransform, false);
        textTransform.anchorMin = Vector2.zero;
        textTransform.anchorMax = Vector2.one;
        textTransform.offsetMin = Vector2.zero;
        textTransform.offsetMax = Vector2.zero;
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 54;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = BodyProbeStatusDisplay.TextColor;
        text.text = BodyProbeStatusDisplay.AppTitle;
        canvasObject.GetComponent<BodyProbeStatusDisplay>().Configure(sender, text);
    }

    private static void EnsureSceneIdentity()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Length != 1 || !scenes[0].enabled || scenes[0].path != ScenePath)
            throw new BuildFailedException(
                "Body probe build must contain only the isolated diagnostic scene.");
        if (PlayerSettings.GetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android) !=
            ConfigureHandUMIBodyProbeProject.PackageIdentifier)
            throw new BuildFailedException("Body probe package identity was overwritten.");
    }

    private static void WriteRuntimeBuildInfo(
        string buildId,
        string sourceCommit,
        string artifactName)
    {
        EnsureFolder("Assets/Resources");
        var info = new BodyProbeBuildInfoAsset
        {
            buildId = buildId,
            sourceCommit = sourceCommit,
            artifactName = artifactName,
            packageIdentifier = ConfigureHandUMIBodyProbeProject.PackageIdentifier,
            metaXrCoreVersion = PackageVersion("Packages/com.meta.xr.sdk.core"),
            metaXrInteractionOvrVersion =
                PackageVersion("Packages/com.meta.xr.sdk.interaction.ovr"),
            openXrPluginVersion = PackageVersion("Packages/com.unity.xr.openxr")
        };
        File.WriteAllText(BuildInfoPath, JsonUtility.ToJson(info, true));
        AssetDatabase.ImportAsset(BuildInfoPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static string ResolveSourceCommit()
    {
        string explicitCommit = Environment.GetEnvironmentVariable(
            "HANDUMI_BODY_PROBE_SOURCE_COMMIT");
        if (!string.IsNullOrWhiteSpace(explicitCommit))
            return explicitCommit.Trim();

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{Directory.GetCurrentDirectory()}\" rev-parse HEAD",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode == 0 && output.Length >= 7)
                    return output;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[BodyProbe] Could not resolve git commit: {exception.Message}");
        }
        return "unavailable";
    }

    private static string PackageVersion(string assetPath)
    {
        UnityEditor.PackageManager.PackageInfo package =
            UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        return package != null ? package.version : "unavailable";
    }

    private static string ComputeSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
            return string.Concat(sha.ComputeHash(stream)
                .Select(value => value.ToString("x2")));
    }

    private static void EnsureFolder(string path)
    {
        string current = null;
        foreach (string segment in path.Split('/'))
        {
            string next = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segment);
            current = next;
        }
    }

    [Serializable]
    private sealed class BodyProbeApkManifest
    {
        public string schema;
        public string buildId;
        public string sourceCommit;
        public string sourceBranch;
        public string artifact;
        public string artifactSha256;
        public ulong artifactSizeBytes;
        public string baselineTag;
        public string baselineCommit;
        public string baselineApkSha256;
        public string builtAtUtc;
        public string unityVersion;
        public string productName;
        public string packageIdentifier;
        public string versionName;
        public int versionCode;
        public string scene;
        public string metaXrCore;
        public string metaXrInteractionOvr;
        public string openXr;
        public string requestedJointSet;
        public string requestedFidelity;
        public string acquisitionSpace;
        public string bodyPermission;
        public string buildResult;
    }
}
