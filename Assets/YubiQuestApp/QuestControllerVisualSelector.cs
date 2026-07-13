using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed class QuestControllerVisualSelector : MonoBehaviour
{
    private const int RuntimeDiscoveryFrames = 120;

    [SerializeField] private OVRInput.Controller controller;
    [SerializeField] private GameObject runtimeVisual;
    [SerializeField] private GameObject packagedFallbackVisual;

    private bool selectionReady;
    private bool useRuntimeVisual;

    public OVRInput.Controller Controller => controller;
    public GameObject RuntimeVisual => runtimeVisual;
    public GameObject PackagedFallbackVisual => packagedFallbackVisual;

    public void Configure(
        OVRInput.Controller targetController,
        GameObject runtimeControllerVisual,
        GameObject fallbackVisual)
    {
        controller = targetController;
        runtimeVisual = runtimeControllerVisual;
        packagedFallbackVisual = fallbackVisual;

        runtimeVisual?.SetActive(false);
        packagedFallbackVisual?.SetActive(false);
    }

    private void Update()
    {
        if (!selectionReady)
            return;

        // A connected-but-sleeping controller reports no valid pose. Rendering
        // it in that state leaves its anchor at the tracking origin (the user's
        // head), which turns the nearby model into a giant screen-filling mesh.
        bool poseTracked =
            OVRInput.GetControllerPositionTracked(controller) &&
            OVRInput.GetControllerOrientationTracked(controller);

        runtimeVisual?.SetActive(poseTracked && useRuntimeVisual);
        packagedFallbackVisual?.SetActive(poseTracked && !useRuntimeVisual);
    }

    private IEnumerator Start()
    {
        for (int frame = 0; frame < RuntimeDiscoveryFrames; ++frame)
        {
            if (TryGetRenderModelPaths(out string[] paths) && paths.Length > 0)
            {
                SelectVisual(paths.Contains(RuntimeModelPath(controller)));
                yield break;
            }

            yield return null;
        }

        SelectVisual(false);
    }

    private void SelectVisual(bool useRuntimeVisual)
    {
        this.useRuntimeVisual = useRuntimeVisual;
        selectionReady = true;
    }

    private static bool TryGetRenderModelPaths(out string[] paths)
    {
        try
        {
            paths = OVRPlugin.GetRenderModelPaths() ?? Array.Empty<string>();
            return true;
        }
        catch
        {
            paths = Array.Empty<string>();
            return false;
        }
    }

    private static string RuntimeModelPath(OVRInput.Controller targetController)
    {
        return targetController == OVRInput.Controller.LTouch
            ? "/model_fb/controller/left"
            : "/model_fb/controller/right";
    }
}
