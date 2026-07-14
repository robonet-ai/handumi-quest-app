using UnityEngine;
using UnityEngine.UI;

public sealed class BodyProbeStatusDisplay : MonoBehaviour
{
    public const string AppTitle = "HandUMI Body Probe";
    public static Color32 BackgroundColor => new Color32(0x10, 0x14, 0x18, 0xff);
    public static Color32 TextColor => new Color32(0x72, 0xe0, 0xd1, 0xff);

    [SerializeField] private BodyProbeSender sender;
    [SerializeField] private Text statusText;
    private string lastText;

    public BodyProbeSender Sender => sender;
    public Text StatusText => statusText;

    public void Configure(BodyProbeSender bodySender, Text text)
    {
        sender = bodySender;
        statusText = text;
        Refresh();
    }

    private void Awake() => Refresh();
    private void Update() => Refresh();

    public void Refresh()
    {
        if (statusText == null)
            return;
        string text = FormatStatus(sender);
        if (text == lastText)
            return;
        statusText.text = text;
        lastText = text;
    }

    public static string FormatStatus(BodyProbeSender value)
    {
        if (value == null)
            return AppTitle + "\nSender unavailable";

        string connection = value.HasConnectedClient ? "Connected" : "Waiting";
        return
            $"{AppTitle}\n" +
            $"Permission: {value.PermissionState}   Body: {(value.BodyActive ? "Active" : "Inactive")}\n" +
            $"Joints: {value.RequestedJointSet} requested / {value.ActiveJointSet} active ({value.JointCount})\n" +
            $"Calibration: {value.CalibrationState}   Fidelity: {value.Fidelity}\n" +
            $"Seq: {value.LastSequence}   {connection}: {value.LocalIpAddress}:{value.ServerPort}";
    }
}
