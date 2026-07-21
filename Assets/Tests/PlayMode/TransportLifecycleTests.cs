using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class TransportLifecycleTests
{
    [UnityTest]
    public IEnumerator BodyProbeSendsManifestBeforeMonotonicPosesAcrossReconnect()
    {
        int port = FreeTcpPort();
        GameObject owner = new GameObject("BodyProbeSender test owner");
        BodyProbeSender sender = owner.AddComponent<BodyProbeSender>();
        SetPrivateField(sender, "serverPort", port);
        yield return null;

        long firstSequence;
        using (var first = new TcpClient())
        {
            first.Connect(IPAddress.Loopback, port);
            yield return WaitForClientCount(sender, 1);
            yield return WaitForNetworkData(first);
            using (var reader = new StreamReader(
                       first.GetStream(),
                       System.Text.Encoding.UTF8,
                       false,
                       4096,
                       true))
            {
                string manifest = reader.ReadLine();
                string pose = reader.ReadLine();
                Assert.That(manifest, Does.Contain(
                    "\"packetType\":\"session_manifest\""));
                Assert.That(pose, Does.Contain("\"packetType\":\"body_pose\""));
                firstSequence = ParseSequence(pose);
            }
        }

        yield return WaitForClientCount(sender, 0);
        using (var second = new TcpClient())
        {
            second.Connect(IPAddress.Loopback, port);
            yield return WaitForClientCount(sender, 1);
            yield return WaitForNetworkData(second);
            using (var reader = new StreamReader(
                       second.GetStream(),
                       System.Text.Encoding.UTF8,
                       false,
                       4096,
                       true))
            {
                string manifest = reader.ReadLine();
                string pose = reader.ReadLine();
                Assert.That(manifest, Does.Contain(
                    "\"packetType\":\"session_manifest\""));
                Assert.That(ParseSequence(pose), Is.GreaterThan(firstSequence));
            }
        }

        UnityEngine.Object.Destroy(owner);
        yield return null;
        var replacement = new TcpListener(IPAddress.Loopback, port);
        Assert.DoesNotThrow(replacement.Start);
        replacement.Stop();
    }

    [UnityTest]
    public IEnumerator PoseServerAcceptsReconnectAndReleasesPortOnDestroy()
    {
        int port = FreeTcpPort();
        GameObject owner = new GameObject("PoseSender test owner");
        PoseSender sender = owner.AddComponent<PoseSender>();
        SetPrivateField(sender, "serverPort", port);

        LogAssert.Expect(
            LogType.Error,
            "[PoseSender] OVRCameraRig/centerEyeAnchor is not wired; pose frames are disabled.");
        yield return null;

        using (var first = new TcpClient())
        {
            first.Connect(IPAddress.Loopback, port);
            Assert.That(first.Connected, Is.True);
            yield return WaitForClientCount(sender, 1);
        }

        yield return WaitForClientCount(sender, 0);

        using (var second = new TcpClient())
        {
            second.Connect(IPAddress.Loopback, port);
            Assert.That(second.Connected, Is.True);
            yield return WaitForClientCount(sender, 1);
        }

        yield return WaitForClientCount(sender, 0);

        UnityEngine.Object.Destroy(owner);
        yield return null;

        var replacement = new TcpListener(IPAddress.Loopback, port);
        Assert.DoesNotThrow(replacement.Start);
        replacement.Stop();
    }

    [UnityTest]
    public IEnumerator PoseServerStopsOnPauseAndRestartsOnceOnResumeAndFocus()
    {
        int port = FreeTcpPort();
        GameObject owner = new GameObject("PoseSender lifecycle owner");
        PoseSender sender = owner.AddComponent<PoseSender>();
        SetPrivateField(sender, "serverPort", port);
        LogAssert.Expect(
            LogType.Error,
            "[PoseSender] OVRCameraRig/centerEyeAnchor is not wired; pose frames are disabled.");
        yield return null;

        InvokePrivate(sender, "OnApplicationPause", true);
        yield return null;
        var duringPause = new TcpListener(IPAddress.Loopback, port);
        Assert.DoesNotThrow(duringPause.Start);
        duringPause.Stop();

        InvokePrivate(sender, "OnApplicationPause", false);
        InvokePrivate(sender, "OnApplicationFocus", true);
        InvokePrivate(sender, "OnApplicationFocus", true);
        yield return null;
        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, port);
            yield return WaitForClientCount(sender, 1);
        }

        UnityEngine.Object.Destroy(owner);
        yield return null;
    }

    [UnityTest]
    public IEnumerator BodyProbeStopsInBackgroundAndRecoversAfterForeground()
    {
        int port = FreeTcpPort();
        GameObject owner = new GameObject("BodyProbeSender lifecycle owner");
        BodyProbeSender sender = owner.AddComponent<BodyProbeSender>();
        SetPrivateField(sender, "serverPort", port);
        yield return null;

        InvokePrivate(sender, "OnApplicationFocus", false);
        yield return null;
        var backgroundListener = new TcpListener(IPAddress.Loopback, port);
        Assert.DoesNotThrow(backgroundListener.Start);
        backgroundListener.Stop();

        InvokePrivate(sender, "OnApplicationFocus", true);
        InvokePrivate(sender, "OnApplicationPause", false);
        yield return null;
        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, port);
            yield return WaitForClientCount(sender, 1);
        }

        UnityEngine.Object.Destroy(owner);
        yield return null;
    }

    [UnityTest]
    public IEnumerator TimeSyncIgnoresMalformedPacketRespondsAndReleasesPort()
    {
        int port = FreeUdpPort();
        GameObject owner = new GameObject("TimeSyncServer test owner");
        TimeSyncServer server = owner.AddComponent<TimeSyncServer>();
        SetPrivateField(server, "syncPort", port);

        yield return null;

        using (var client = new UdpClient())
        {
            client.Connect(IPAddress.Loopback, port);
            client.Client.ReceiveTimeout = 200;
            client.Send(new byte[] { 99 }, 1);
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
            Assert.Throws<SocketException>(() => client.Receive(ref endpoint));

            const long pcTimeNs = 123456789012345L;
            byte[] request = new byte[HandUMIWireProtocol.TimeSyncRequestSize];
            request[0] = HandUMIWireProtocol.TimeSyncRequestId;
            Buffer.BlockCopy(BitConverter.GetBytes(pcTimeNs), 0, request, 1, 8);
            client.Send(request, request.Length);

            byte[] response = client.Receive(ref endpoint);
            Assert.That(response, Has.Length.EqualTo(
                HandUMIWireProtocol.TimeSyncResponseSize));
            Assert.That(response[0], Is.EqualTo(
                HandUMIWireProtocol.TimeSyncResponseId));
            Assert.That(BitConverter.ToInt64(response, 1), Is.EqualTo(pcTimeNs));
            // OVRPlugin's runtime clock is zero in a headless Editor. Device
            // hardware validation separately requires a positive timestamp.
            Assert.That(BitConverter.ToInt64(response, 9),
                Is.GreaterThanOrEqualTo(0));
        }

        UnityEngine.Object.Destroy(owner);
        yield return null;

        Assert.DoesNotThrow(() =>
        {
            using (var replacement = new UdpClient(port))
            {
            }
        });
    }

    private static void SetPrivateField<T>(T target, string fieldName, object value)
    {
        FieldInfo field = typeof(T).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate<T>(T target, string methodName, object argument)
    {
        MethodInfo method = typeof(T).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing private method {methodName}");
        method.Invoke(target, new[] { argument });
    }

    private static IEnumerator WaitForClientCount(PoseSender sender, int expected)
    {
        float deadline = Time.realtimeSinceStartup + 2f;
        while (sender.ConnectedClientCount != expected &&
               Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(sender.ConnectedClientCount, Is.EqualTo(expected));
    }

    private static IEnumerator WaitForClientCount(BodyProbeSender sender, int expected)
    {
        float deadline = Time.realtimeSinceStartup + 2f;
        while (sender.ConnectedClientCount != expected &&
               Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(sender.ConnectedClientCount, Is.EqualTo(expected));
    }

    private static IEnumerator WaitForNetworkData(TcpClient client)
    {
        float deadline = Time.realtimeSinceStartup + 2f;
        while (client.Available == 0 && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(client.Available, Is.GreaterThan(0));
    }

    private static long ParseSequence(string json)
    {
        const string marker = "\"seq\":";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        start += marker.Length;
        int end = start;
        while (end < json.Length && (json[end] == '-' || char.IsDigit(json[end])))
            ++end;
        return long.Parse(json.Substring(start, end - start));
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int FreeUdpPort()
    {
        using (var socket = new UdpClient(0))
            return ((IPEndPoint)socket.Client.LocalEndPoint).Port;
    }
}
