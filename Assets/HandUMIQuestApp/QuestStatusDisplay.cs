using UnityEngine;
using UnityEngine.UI;

public sealed class QuestStatusDisplay : MonoBehaviour
{
    public const string AppTitle = "HandUMI Quest App (v0.2.1)";
    public const string BackgroundHtml = "#191919";
    public const string TextHtml = "#AF0000";
    public const float NearClipMeters = 0.05f;

    [SerializeField] private PoseSender poseSender;
    [SerializeField] private Text statusText;

    private string lastRenderedText;

    public PoseSender PoseSender => poseSender;
    public Text StatusText => statusText;
    public static Color32 BackgroundColor => new Color32(0x19, 0x19, 0x19, 0xff);
    public static Color32 TextColor => new Color32(0xaf, 0x00, 0x00, 0xff);

    public void Configure(PoseSender sender, Text text)
    {
        poseSender = sender;
        statusText = text;
        Refresh();
    }

    private void Awake()
    {
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (statusText == null)
            return;

        string next = FormatStatus(
            poseSender != null && poseSender.HasConnectedClient,
            poseSender != null ? poseSender.LocalIpAddress : null,
            poseSender != null ? poseSender.ServerPort : HandUMIWireProtocol.PosePort);
        if (next == lastRenderedText)
            return;

        statusText.text = next;
        lastRenderedText = next;
    }

    public static string FormatStatus(bool connected, string localIpAddress, int port)
    {
        if (!connected)
            return AppTitle;

        string address = string.IsNullOrWhiteSpace(localIpAddress)
            ? "Unavailable"
            : localIpAddress;
        return $"{AppTitle}\nConnected • IP: {address}:{port}";
    }
}
