using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Pure wire-format helpers shared by the runtime servers and EditMode tests.
/// Keeping framing here makes the legacy HandUMI/YUBI compatibility contract
/// testable without a Quest runtime.
/// </summary>
public static class YubiWireProtocol
{
    public const int PosePort = 65432;
    public const int TimeSyncPort = 42000;
    public const int TimeSyncRequestSize = 9;
    public const int TimeSyncResponseSize = 17;
    public const byte TimeSyncRequestId = 1;
    public const byte TimeSyncResponseId = 2;

    public static byte[] EncodePoseFrame(PoseData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        return Encoding.UTF8.GetBytes(JsonUtility.ToJson(data) + "\n");
    }

    public static bool TryParseTimeSyncRequest(byte[] request, out long pcTimeNs)
    {
        pcTimeNs = 0;
        if (request == null || request.Length != TimeSyncRequestSize ||
            request[0] != TimeSyncRequestId)
            return false;

        pcTimeNs = BitConverter.ToInt64(request, 1);
        return true;
    }

    public static byte[] EncodeTimeSyncResponse(long pcTimeNs, long questTimeNs)
    {
        byte[] response = new byte[TimeSyncResponseSize];
        response[0] = TimeSyncResponseId;
        Buffer.BlockCopy(BitConverter.GetBytes(pcTimeNs), 0, response, 1, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(questTimeNs), 0, response, 9, 8);
        return response;
    }
}
