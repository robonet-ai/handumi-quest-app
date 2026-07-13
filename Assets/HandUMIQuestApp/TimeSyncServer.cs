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
    [SerializeField] private int syncPort = HandUMIWireProtocol.TimeSyncPort;

    private UdpClient sock;

    private void Start()
    {
        if (sock != null)
            return;

        sock = new UdpClient(syncPort);
        sock.BeginReceive(OnRequest, null);
        Debug.Log($"[TimeSyncServer] listening on *:{syncPort}");
    }

    private void OnDestroy()
    {
        UdpClient socket = sock;
        sock = null;
        socket?.Close();
    }

    private void OnRequest(IAsyncResult ar)
    {
        UdpClient socket = sock;
        if (socket == null)
            return;

        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        byte[] request;

        try
        {
            request = socket.EndReceive(ar, ref remote);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (SocketException)
        {
            ContinueReceive(socket);
            return;
        }

        // Little-endian request: [id=1:u8][t1_pc_ns:i64]
        if (HandUMIWireProtocol.TryParseTimeSyncRequest(request, out long t1PcNs))
        {
            long t2QuestNs = QuestTimeNs();

            // Little-endian response:
            // [id=2:u8][t1_echo:i64][t2_quest_ns:i64]
            byte[] response =
                HandUMIWireProtocol.EncodeTimeSyncResponse(t1PcNs, t2QuestNs);
            try
            {
                socket.Send(response, response.Length, remote);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                ContinueReceive(socket);
                return;
            }
        }

        ContinueReceive(socket);
    }

    private void ContinueReceive(UdpClient socket)
    {
        if (!ReferenceEquals(sock, socket))
            return;

        try
        {
            socket.BeginReceive(OnRequest, null);
        }
        catch (ObjectDisposedException)
        {
            // Expected during scene teardown/application shutdown.
        }
        catch (SocketException)
        {
            // The next scene/app lifecycle owns any restart attempt.
        }
    }

    private static long QuestTimeNs()
    {
        return (long)(OVRPlugin.GetTimeInSeconds() * 1_000_000_000.0);
    }
}
