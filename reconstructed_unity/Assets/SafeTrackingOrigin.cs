// Reconstructed from the reference APK.
// NOT the original AIRoA source code.
//
// CONFIRMED: source path Assets/SafeTrackingOrigin.cs; class/field/method
// names; coroutine locals; XR tracking-origin APIs; diagnostic log format.
// APPROXIMATE: retry duration and exact mode preference control flow.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public sealed class SafeTrackingOrigin : MonoBehaviour
{
    [Tooltip("Re-apply the origin when the runtime notifies an origin change.")]
    [SerializeField] private bool listenForOriginChanges = true;

    private readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();

    private IEnumerator Start()
    {
        // The recovered coroutine contains locals named `inputs` and `wait`
        // and waits for XR input subsystems before applying the origin.
        float wait = 0f;
        while (wait <= 5f)
        {
            SubsystemManager.GetInstances(inputs);
            if (inputs.Count > 0)
                break;

            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        ApplyBestOrigin();

        if (listenForOriginChanges)
        {
            foreach (XRInputSubsystem input in inputs)
                input.trackingOriginUpdated += OnTrackingOriginUpdated;
        }
    }

    private void OnDestroy()
    {
        foreach (XRInputSubsystem input in inputs)
        {
            if (input != null)
                input.trackingOriginUpdated -= OnTrackingOriginUpdated;
        }
    }

    private void OnTrackingOriginUpdated(XRInputSubsystem _)
    {
        ApplyBestOrigin();
    }

    private void ApplyBestOrigin()
    {
        SubsystemManager.GetInstances(inputs);

        foreach (XRInputSubsystem input in inputs)
        {
            TrackingOriginModeFlags supported = input.GetSupportedTrackingOriginModes();
            TrackingOriginModeFlags tried =
                (supported & TrackingOriginModeFlags.Floor) != 0
                    ? TrackingOriginModeFlags.Floor
                    : TrackingOriginModeFlags.Device;

            bool ok = input.TrySetTrackingOriginMode(tried);
            TrackingOriginModeFlags actual = input.GetTrackingOriginMode();

            Debug.Log($"[SafeTrackingOrigin] Supported={supported}  Tried={tried}  Ok={ok}  Actual={actual}");
        }
    }
}
