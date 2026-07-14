using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Requests Meta body permission before enabling OVRBody. Keeping OVRBody
/// disabled until the callback avoids a first-frame StartBodyTracking2 failure
/// that would otherwise leave the component disabled after permission is granted.
/// </summary>
public sealed class BodyProbePermissionController : MonoBehaviour
{
    public const string PermissionId = "com.oculus.permission.BODY_TRACKING";

    [SerializeField] private OVRBody body;
    [SerializeField] private string permissionState = "NotRequested";

#if UNITY_ANDROID && !UNITY_EDITOR
    private PermissionCallbacks callbacks;
#endif

    public OVRBody Body => body;
    public string PermissionState => permissionState;
    public bool IsGranted => permissionState == "Granted";

    public void Configure(OVRBody bodyComponent)
    {
        body = bodyComponent;
        if (body != null)
            body.enabled = false;
    }

    private void Awake()
    {
        if (body != null)
            body.enabled = false;
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(PermissionId))
        {
            SetGranted();
            return;
        }

        permissionState = "Requested";
        callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => SetGranted();
        callbacks.PermissionDenied += _ => SetDenied();
        callbacks.PermissionDeniedAndDontAskAgain += _ => SetDenied();
        Permission.RequestUserPermission(PermissionId, callbacks);
#else
        permissionState = "EditorGranted";
        if (body != null)
            body.enabled = true;
#endif
    }

    private void SetGranted()
    {
        permissionState = "Granted";
        if (body != null)
            body.enabled = true;
    }

    private void SetDenied()
    {
        permissionState = "Denied";
        if (body != null)
            body.enabled = false;
    }
}
