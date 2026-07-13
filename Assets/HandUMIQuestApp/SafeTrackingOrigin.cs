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
            SubsystemManager.GetSubsystems(inputs);
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

    private void OnTrackingOriginUpdated(XRInputSubsystem input)
    {
        if (input == null)
            return;

        TrackingOriginModeFlags supported = input.GetSupportedTrackingOriginModes();
        TrackingOriginModeFlags preferred =
            (supported & TrackingOriginModeFlags.Floor) != 0
                ? TrackingOriginModeFlags.Floor
                : TrackingOriginModeFlags.Device;

        // Meta XR can notify every frame even though the active origin has not
        // changed. Re-apply only after a real departure from the preferred
        // origin so collection runs do not flood logcat or feed back into XR.
        if (input.GetTrackingOriginMode() != preferred)
            ApplyBestOrigin();
    }

    private void ApplyBestOrigin()
    {
        SubsystemManager.GetSubsystems(inputs);

        foreach (XRInputSubsystem input in inputs)
        {
            TrackingOriginModeFlags supported = input.GetSupportedTrackingOriginModes();
            TrackingOriginModeFlags tried =
                (supported & TrackingOriginModeFlags.Floor) != 0
                    ? TrackingOriginModeFlags.Floor
                    : TrackingOriginModeFlags.Device;

            TrackingOriginModeFlags actual = input.GetTrackingOriginMode();
            // Some runtimes emit trackingOriginUpdated even when setting the
            // already-active mode. Avoid feeding that notification back into
            // TrySetTrackingOriginMode every frame.
            bool ok = actual == tried || input.TrySetTrackingOriginMode(tried);
            actual = input.GetTrackingOriginMode();

            Debug.Log($"[SafeTrackingOrigin] Supported={supported}  Tried={tried}  Ok={ok}  Actual={actual}");
        }
    }
}
