// Reconstructed from the reference APK.
// NOT the original AIRoA source code.
//
// CONFIRMED: source path Assets/TimeSyncServer.cs; class, field and method
// names; default port 42000; 9-byte request and 17-byte response layouts.

using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public sealed class TimeSyncServer : MonoBehaviour
{
    [Tooltip("UDP port used by the PC sync thread (default 42000).")]
    [SerializeField] private int syncPort = 42000;

    private UdpClient sock;

    private void Start()
    {
        sock = new UdpClient(syncPort);
        sock.BeginReceive(OnRequest, null);
        Debug.Log($"[TimeSyncServer] listening on *:{syncPort}");
    }

    private void OnDestroy()
    {
        sock?.Close();
    }

    private void OnRequest(IAsyncResult ar)
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        byte[] request;

        try
        {
            request = sock.EndReceive(ar, ref remote);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (SocketException)
        {
            if (sock != null)
                sock.BeginReceive(OnRequest, null);
            return;
        }

        // Little-endian request: [id=1:u8][t1_pc_ns:i64]
        if (request.Length == 9 && request[0] == 1)
        {
            long t1PcNs = BitConverter.ToInt64(request, 1);
            long t2QuestNs = QuestTimeNs();

            // Little-endian response:
            // [id=2:u8][t1_echo:i64][t2_quest_ns:i64]
            byte[] response = new byte[17];
            response[0] = 2;
            Buffer.BlockCopy(request, 1, response, 1, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(t2QuestNs), 0, response, 9, 8);
            sock.Send(response, response.Length, remote);
        }

        sock.BeginReceive(OnRequest, null);
    }

    private static long QuestTimeNs()
    {
        return (long)(OVRPlugin.GetTimeInSeconds() * 1_000_000_000.0);
    }
}
