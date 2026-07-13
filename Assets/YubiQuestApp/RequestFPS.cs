// Reconstructed from YubiQuestApp-v0.1.0.apk.
// NOT the original AIRoA source code.
//
// CONFIRMED: source path Assets/RequestFPS.cs; class QuestRefresh120; Awake;
// refresh-change callback; candidate frequencies 120, 90 and 72 Hz; log text.

using System.Linq;
using UnityEngine;

public sealed class QuestRefresh120 : MonoBehaviour
{
    private void Awake()
    {
        float[] available = OVRPlugin.systemDisplayFrequenciesAvailable;
        float targetHz = 72f;

        if (available != null)
        {
            if (available.Contains(120f))
                targetHz = 120f;
            else if (available.Contains(90f))
                targetHz = 90f;
        }

        OVRPlugin.systemDisplayFrequency = targetHz;
        Application.targetFrameRate = Mathf.RoundToInt(targetHz);
        QualitySettings.vSyncCount = 0;

        OVRManager.DisplayRefreshRateChanged += (fromHz, toHz) =>
            Debug.Log($"Refresh changed {fromHz} -> {toHz}");
    }
}
