using System;
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

/// <summary>
/// Repeatably reconstructs the minimal compatibility scene and exposes a
/// batch-mode Android build entry point.
/// </summary>
public static class HandUMICompatibilityBuild
{
    public const string ScenePath =
        "Assets/HandUMIQuestApp/Scenes/HandUMIQuestCompatibility.unity";
    public const string DefaultApkPath =
        "Builds/Android/handumi-quest-app-v0.2.1.apk";

    private const string HandPrefabPath =
        "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";
    private const string RuntimeControllerPrefabPath =
        "Packages/com.meta.xr.sdk.core/Prefabs/OVRRuntimeControllerPrefab.prefab";
    private const string PackagedControllerPrefabPath =
        "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";
    private const string MetaLitShaderPath =
        "Packages/com.meta.xr.sdk.core/Shaders/MetaLit.shader";
    private const string GeneratedUiMaterialPath =
        "Assets/HandUMIQuestApp/Generated/HandUMIStatusText.mat";

    private const string OriginalApkSha256 =
        "1af91c35e0476b629d85f87bed33667a37b08ecba934596f8420069ab7174";

    [MenuItem("Tools/HandUMI Quest/Rebuild Compatibility Scene")]
    public static void RebuildCompatibilityScene()
    {
        ConfigureHandUMIQuestProject.Apply();
        EnsureFolder("Assets/HandUMIQuestApp/Scenes");

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "HandUMIQuestCompatibility";

        GameObject managerObject = new GameObject("OVRManager");
        OVRManager manager = managerObject.AddComponent<OVRManager>();
        // Stage is the OpenXR floor space without recentering. Meta XR 74 can
        // report Stage for an active Floor space; serializing FloorLevel makes
        // OVRManager re-apply the origin and recreate app space every frame.
        manager.trackingOriginType = OVRManager.TrackingOrigin.Stage;

        GameObject rigObject = new GameObject("OVRCameraRig");
        OVRCameraRig rig = rigObject.AddComponent<OVRCameraRig>();
        rig.EnsureGameObjectIntegrity();
        ConfigureBackground(rig);
        CreateTrackedHands(rig);
        CreateControllerVisuals(rig);

        GameObject services = new GameObject("HandUMIQuestServices");
        PoseSender poseSender = services.AddComponent<PoseSender>();
        services.AddComponent<TimeSyncServer>();
        services.AddComponent<QuestRefresh120>();
        services.AddComponent<SafeTrackingOrigin>();

        SerializedObject serializedSender = new SerializedObject(poseSender);
        serializedSender.FindProperty("ovrCameraRig").objectReferenceValue = rig;
        serializedSender.ApplyModifiedPropertiesWithoutUndo();

        CreateStatusCanvas(rig, poseSender);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException($"Could not save {ScenePath}");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log($"[HandUMI Quest] Rebuilt compatibility scene: {ScenePath}");
    }

    private static void ConfigureBackground(OVRCameraRig rig)
    {
        foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QuestStatusDisplay.BackgroundColor;
            camera.nearClipPlane = QuestStatusDisplay.NearClipMeters;
        }
    }

    private static void CreateTrackedHands(OVRCameraRig rig)
    {
        GameObject handPrefab = LoadRequiredPrefab(HandPrefabPath);
        CreateHand(handPrefab, rig.leftHandAnchor, "LeftTrackedHand",
            OVRHand.Hand.HandLeft, OVRSkeleton.SkeletonType.HandLeft,
            OVRMesh.MeshType.HandLeft);
        CreateHand(handPrefab, rig.rightHandAnchor, "RightTrackedHand",
            OVRHand.Hand.HandRight, OVRSkeleton.SkeletonType.HandRight,
            OVRMesh.MeshType.HandRight);
    }

    private static void CreateHand(
        GameObject prefab,
        Transform parent,
        string name,
        OVRHand.Hand hand,
        OVRSkeleton.SkeletonType skeleton,
        OVRMesh.MeshType mesh)
    {
        GameObject instance = InstantiatePrefab(prefab, parent, name);

        SerializedObject serializedHand = new SerializedObject(instance.GetComponent<OVRHand>());
        serializedHand.FindProperty("HandType").intValue = (int)hand;
        serializedHand.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedSkeleton =
            new SerializedObject(instance.GetComponent<OVRSkeleton>());
        serializedSkeleton.FindProperty("_skeletonType").intValue = (int)skeleton;
        serializedSkeleton.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedMesh = new SerializedObject(instance.GetComponent<OVRMesh>());
        serializedMesh.FindProperty("_meshType").intValue = (int)mesh;
        serializedMesh.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateControllerVisuals(OVRCameraRig rig)
    {
        GameObject runtimePrefab = LoadRequiredPrefab(RuntimeControllerPrefabPath);
        GameObject packagedPrefab = LoadRequiredPrefab(PackagedControllerPrefabPath);
        Shader controllerShader = AssetDatabase.LoadAssetAtPath<Shader>(MetaLitShaderPath);
        if (controllerShader == null)
            throw new InvalidOperationException(
                $"Required URP controller shader is missing: {MetaLitShaderPath}");

        CreateControllerVisual(rig.leftControllerAnchor, "LeftControllerVisual",
            OVRInput.Controller.LTouch, runtimePrefab, packagedPrefab, controllerShader);
        CreateControllerVisual(rig.rightControllerAnchor, "RightControllerVisual",
            OVRInput.Controller.RTouch, runtimePrefab, packagedPrefab, controllerShader);
    }

    private static void CreateControllerVisual(
        Transform parent,
        string name,
        OVRInput.Controller controller,
        GameObject runtimePrefab,
        GameObject packagedPrefab,
        Shader controllerShader)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        QuestControllerVisualSelector selector =
            root.AddComponent<QuestControllerVisualSelector>();

        GameObject runtime = InstantiatePrefab(
            runtimePrefab, root.transform, name + "Runtime");
        OVRRuntimeController runtimeController = runtime.GetComponent<OVRRuntimeController>();
        runtimeController.m_controller = controller;
        runtimeController.m_supportAnimation = true;
        // The Meta prefab defaults to Unity's legacy Standard shader. Standard
        // has no URP pass and therefore renders the runtime GLTF as magenta.
        runtimeController.m_controllerModelShader = controllerShader;

        GameObject fallback = InstantiatePrefab(
            packagedPrefab, root.transform, name + "PackagedFallback");
        fallback.GetComponent<OVRControllerHelper>().m_controller = controller;
        MakeFallbackMaterialsUrpCompatible(fallback, name, controllerShader);

        selector.Configure(controller, runtime, fallback);
    }

    private static void MakeFallbackMaterialsUrpCompatible(
        GameObject fallback,
        string controllerName,
        Shader controllerShader)
    {
        EnsureFolder("Assets/HandUMIQuestApp/Generated");
        Renderer[] renderers = fallback.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; ++materialIndex)
            {
                Material source = materials[materialIndex];
                if (source == null)
                    continue;

                string path =
                    $"Assets/HandUMIQuestApp/Generated/{controllerName}-" +
                    $"{rendererIndex}-{materialIndex}.mat";
                Material converted = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (converted == null)
                {
                    converted = new Material(controllerShader)
                    {
                        name = $"{controllerName}-{rendererIndex}-{materialIndex}"
                    };
                    AssetDatabase.CreateAsset(converted, path);
                }

                converted.shader = controllerShader;
                converted.color = source.HasProperty("_BaseColor")
                    ? source.GetColor("_BaseColor")
                    : source.HasProperty("_Color")
                        ? source.GetColor("_Color")
                        : Color.white;
                converted.mainTexture = source.HasProperty("_BaseMap")
                    ? source.GetTexture("_BaseMap")
                    : source.HasProperty("_MainTex")
                        ? source.GetTexture("_MainTex")
                        : null;
                if (converted.HasProperty("_Metallic"))
                    converted.SetFloat("_Metallic", 0f);
                if (converted.HasProperty("_Smoothness"))
                    converted.SetFloat("_Smoothness", 0.45f);
                EditorUtility.SetDirty(converted);
                materials[materialIndex] = converted;
            }

            renderers[rendererIndex].sharedMaterials = materials;
        }
    }

    private static void CreateStatusCanvas(OVRCameraRig rig, PoseSender poseSender)
    {
        GameObject canvasObject = new GameObject(
            "HandUMIStatusCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(QuestStatusDisplay));
        RectTransform canvasTransform = canvasObject.GetComponent<RectTransform>();
        canvasTransform.SetParent(rig.centerEyeAnchor, false);
        canvasTransform.localPosition = new Vector3(0f, -0.08f, 1.25f);
        canvasTransform.localRotation = Quaternion.identity;
        canvasTransform.localScale = Vector3.one * 0.0015f;
        canvasTransform.sizeDelta = new Vector2(1000f, 280f);

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
        text.text = QuestStatusDisplay.AppTitle;
        text.color = QuestStatusDisplay.TextColor;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.material = GetOrCreateStatusTextMaterial();
        text.fontSize = 72;
        text.raycastTarget = false;

        canvasObject.GetComponent<QuestStatusDisplay>().Configure(poseSender, text);
    }

    private static Material GetOrCreateStatusTextMaterial()
    {
        Shader uiShader = Shader.Find("UI/Default");
        if (uiShader == null)
            throw new InvalidOperationException("Unity UI/Default shader is unavailable.");

        EnsureFolder("Assets/HandUMIQuestApp/Generated");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(
            GeneratedUiMaterialPath);
        if (material == null)
        {
            material = new Material(uiShader) { name = "HandUMIStatusText" };
            AssetDatabase.CreateAsset(material, GeneratedUiMaterialPath);
        }
        else if (material.shader != uiShader)
        {
            material.shader = uiShader;
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    private static GameObject LoadRequiredPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new InvalidOperationException($"Required Meta XR prefab is missing: {path}");
        return prefab;
    }

    private static GameObject InstantiatePrefab(
        GameObject prefab,
        Transform parent,
        string name)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        if (instance == null)
            throw new InvalidOperationException($"Could not instantiate {prefab.name}");

        instance.name = name;
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        return instance;
    }

    [MenuItem("Tools/HandUMI Quest/Build Compatibility APK")]
    public static void BuildAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            throw new InvalidOperationException(
                "Android must be the active build target. Use -buildTarget Android in batch mode.");
        }

        ConfigureAndroidTools.Apply();
        ConfigureHandUMIQuestProject.Apply();
        EnsureCompatibilitySceneEnabled();

        string requestedPath = Environment.GetEnvironmentVariable("HANDUMI_APK_OUTPUT");
        string outputPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(requestedPath) ? DefaultApkPath : requestedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        string[] scenes = EditorBuildSettings.scenes
            .Where(candidate => candidate.enabled)
            .Select(candidate => candidate.path)
            .ToArray();
        if (scenes.Length != 1 || scenes[0] != ScenePath)
            throw new BuildFailedException("Compatibility build must contain exactly the reconstructed scene.");

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                $"Android build failed with {report.summary.totalErrors} errors.");
        }

        string sha256 = ComputeSha256(outputPath);
        WriteBuildEvidence(outputPath, sha256, report);
        Debug.Log(
            $"[HandUMI Quest] Built {outputPath} ({report.summary.totalSize} bytes, sha256={sha256})");
    }

    private static void WriteBuildEvidence(
        string outputPath,
        string sha256,
        BuildReport report)
    {
        var manifest = new CompatibilityBuildManifest
        {
            schema = "handumi_quest_compatibility_build_v1",
            artifact = Path.GetFileName(outputPath),
            artifactSha256 = sha256,
            originalApkSha256 = OriginalApkSha256,
            originalPackageIdentifier =
                "com.UnityTechnologies.com.unity.template.urpblank",
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            unityVersion = Application.unityVersion,
            androidSdk = ConfigureAndroidTools.ResolvedSdkPath,
            androidNdk = ConfigureAndroidTools.ResolvedNdkPath,
            jdk = ConfigureAndroidTools.ResolvedJdkPath,
            productName = PlayerSettings.productName,
            packageIdentifier = PlayerSettings.GetApplicationIdentifier(
                NamedBuildTarget.Android),
            versionName = PlayerSettings.bundleVersion,
            versionCode = PlayerSettings.Android.bundleVersionCode,
            scriptingBackend = PlayerSettings.GetScriptingBackend(
                NamedBuildTarget.Android).ToString(),
            architecture = PlayerSettings.Android.targetArchitectures.ToString(),
            graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)
                .Select(api => api.ToString())
                .ToArray(),
            scene = ScenePath,
            metaXrCore = PackageVersion("Packages/com.meta.xr.sdk.core"),
            metaXrInteractionOvr = PackageVersion(
                "Packages/com.meta.xr.sdk.interaction.ovr"),
            openXr = PackageVersion("Packages/com.unity.xr.openxr"),
            buildResult = report.summary.result.ToString(),
            artifactSizeBytes = (ulong)new FileInfo(outputPath).Length,
            buildReportTotalSizeBytes = report.summary.totalSize
        };

        File.WriteAllText(
            outputPath + ".manifest.json",
            JsonUtility.ToJson(manifest, true) + Environment.NewLine);
        File.WriteAllText(
            outputPath + ".sha256",
            $"{sha256}  {Path.GetFileName(outputPath)}{Environment.NewLine}");
    }

    private static void EnsureCompatibilitySceneEnabled()
    {
        if (!File.Exists(ScenePath))
        {
            RebuildCompatibilityScene();
            return;
        }

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
    }

    private static string PackageVersion(string assetPath)
    {
        UnityEditor.PackageManager.PackageInfo package =
            UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
        return package == null ? "unknown" : package.version;
    }

    private static string ComputeSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 hash = SHA256.Create())
        {
            return string.Concat(hash.ComputeHash(stream)
                .Select(value => value.ToString("x2")));
        }
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

    [Serializable]
    private sealed class CompatibilityBuildManifest
    {
        public string schema;
        public string artifact;
        public string artifactSha256;
        public string originalApkSha256;
        public string originalPackageIdentifier;
        public string builtAtUtc;
        public string unityVersion;
        public string androidSdk;
        public string androidNdk;
        public string jdk;
        public string productName;
        public string packageIdentifier;
        public string versionName;
        public int versionCode;
        public string scriptingBackend;
        public string architecture;
        public string[] graphicsApis;
        public string scene;
        public string metaXrCore;
        public string metaXrInteractionOvr;
        public string openXr;
        public string buildResult;
        public ulong artifactSizeBytes;
        public ulong buildReportTotalSizeBytes;
    }
}
