// <copyright file="TcpNmeaBroadcasterTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Broadcaster.Tests;

[TestClass]
public class TcpNmeaBroadcasterTests
{
    private const string Sentence1 = "!AIVDM,1,1,,B,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
    private const string Sentence2 = "!AIVDM,1,1,,A,15MgK45P3@G?fl0E`JbR0OwT0@MS,0*4E";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task StartAsync_PortZero_BindsAFreePortAndExposesTheEndpoint()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster();

        await broadcaster.StartAsync(cts.Token);

        broadcaster.LocalEndPoint.Port.ShouldBeGreaterThan(0);
        broadcaster.LocalEndPoint.Address.ShouldBe(IPAddress.Loopback);
        broadcaster.Name.ShouldBe($"tcp://127.0.0.1:{broadcaster.LocalEndPoint.Port}");
        broadcaster.ClientCount.ShouldBe(0);

        await using TcpNmeaConnection client = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        client.Connected.ShouldBeTrue();
    }

    [TestMethod]
    public void LocalEndPoint_BeforeStart_Throws()
    {
        TcpNmeaBroadcaster broadcaster = CreateBroadcaster();

        Should.Throw<InvalidOperationException>(() => broadcaster.LocalEndPoint);
        broadcaster.Name.ShouldBe("tcp://127.0.0.1:0");
    }

    [TestMethod]
    public async Task BroadcastAsync_ReachesEveryConnectedClient()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        await using TcpNmeaConnection client1 = await ConnectAsync(broadcaster, cts.Token);
        await using TcpNmeaConnection client2 = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 2, cts.Token);

        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence2), cts.Token);

        foreach (TcpNmeaConnection client in new[] { client1, client2 })
        {
            (await ReadStringAsync(client, cts.Token)).ShouldBe(Sentence1);
            (await ReadStringAsync(client, cts.Token)).ShouldBe(Sentence2);
        }
    }

    [TestMethod]
    [DataRow(NmeaLineEnding.Lf, "\n")]
    [DataRow(NmeaLineEnding.CrLf, "\r\n")]
    public async Task BroadcastAsync_TerminatesEachSentenceWithTheConfiguredLineEnding(NmeaLineEnding lineEnding, string terminator)
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster(new TcpBroadcasterOptions { Address = IPAddress.Loopback, LineEnding = lineEnding });
        await broadcaster.StartAsync(cts.Token);
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, broadcaster.LocalEndPoint.Port, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence2), cts.Token);

        string expected = Sentence1 + terminator + Sentence2 + terminator;
        (await ReadExactlyAsync(client.GetStream(), expected.Length, cts.Token)).ShouldBe(expected);
    }

    [TestMethod]
    public async Task DisconnectAllAsync_ClosesClientsAndKeepsListening()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        await using TcpNmeaConnection client1 = await ConnectAsync(broadcaster, cts.Token);
        await using TcpNmeaConnection client2 = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 2, cts.Token);

        await broadcaster.DisconnectAllAsync(cts.Token);

        broadcaster.ClientCount.ShouldBe(0);
        (await client1.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
        (await client2.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();

        // The receiver reconnects, as it would after a dropped feed.
        await client1.ConnectAsync(cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        (await ReadStringAsync(client1, cts.Token)).ShouldBe(Sentence1);
    }

    [TestMethod]
    public async Task ClientThatDisconnects_IsRemoved()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        await using TcpNmeaConnection staying = await ConnectAsync(broadcaster, cts.Token);
        TcpNmeaConnection leaving = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 2, cts.Token);

        await leaving.DisposeAsync();

        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        (await ReadStringAsync(staying, cts.Token)).ShouldBe(Sentence1);
    }

    [TestMethod]
    public async Task BroadcastAsync_WithNoClients_IsANoOp()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster();

        // Before start, and after start with nobody connected.
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.StartAsync(cts.Token);
        ValueTask broadcast = broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);

        broadcast.IsCompletedSuccessfully.ShouldBeTrue();
        broadcaster.ClientCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task StopAsync_ClosesClientsAndStopsListening()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        int port = broadcaster.LocalEndPoint.Port;
        await using TcpNmeaConnection client = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        await broadcaster.StopAsync(cts.Token);

        broadcaster.ClientCount.ShouldBe(0);
        (await client.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
        await using TcpNmeaConnection late = new("127.0.0.1", port);
        await Should.ThrowAsync<SocketException>(late.ConnectAsync(cts.Token));
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        TcpNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);

        await broadcaster.DisposeAsync();
        await broadcaster.DisposeAsync();
        await broadcaster.StopAsync(cts.Token);

        // Never started is fine too.
        TcpNmeaBroadcaster unstarted = CreateBroadcaster();
        await unstarted.DisposeAsync();
        await unstarted.DisposeAsync();
    }

    [TestMethod]
    public async Task SlowClient_WithDropOldest_SkipsAheadToTheMostRecentSentencesAndStaysConnected()
    {
        const int sentenceCount = 20_000;
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster(new TcpBroadcasterOptions
        {
            Address = IPAddress.Loopback,
            ClientQueueCapacity = 2,
            SlowClient = SlowClientPolicy.DropOldest,
            LineEnding = NmeaLineEnding.Lf,
        });
        await broadcaster.StartAsync(cts.Token);
        using TcpClient client = await ConnectSlowClientAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        // The client reads nothing while the whole burst is broadcast, so its socket buffers fill
        // and all but the newest queued sentences are dropped.
        for (int i = 0; i < sentenceCount; i++)
        {
            await broadcaster.BroadcastAsync(LargeSentence(i), cts.Token);
        }

        List<int> received = [];
        using StreamReader reader = new(client.GetStream(), Encoding.ASCII);
        while (received.Count == 0 || received[^1] != sentenceCount - 1)
        {
            string? line = await reader.ReadLineAsync(cts.Token);
            line.ShouldNotBeNull();
            received.Add(int.Parse(line.AsSpan(0, 8), provider: null));
        }

        broadcaster.ClientCount.ShouldBe(1);
        received.Count.ShouldBeLessThan(sentenceCount);
        received.ShouldBeInOrder(SortDirection.Ascending);
        received[^2].ShouldBe(sentenceCount - 2);
    }

    [TestMethod]
    public async Task SlowClient_WithDisconnect_IsClosed()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using TcpNmeaBroadcaster broadcaster = CreateBroadcaster(new TcpBroadcasterOptions
        {
            Address = IPAddress.Loopback,
            ClientQueueCapacity = 2,
            SlowClient = SlowClientPolicy.Disconnect,
        });
        await broadcaster.StartAsync(cts.Token);
        using TcpClient client = await ConnectSlowClientAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        for (int i = 0; i < 100_000 && broadcaster.ClientCount > 0; i++)
        {
            await broadcaster.BroadcastAsync(LargeSentence(i), cts.Token);
        }

        broadcaster.ClientCount.ShouldBe(0);

        // Whatever was already in flight drains, then the connection ends.
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[64 * 1024];
        bool ended = false;
        while (!ended)
        {
            try
            {
                ended = await stream.ReadAsync(buffer, cts.Token) == 0;
            }
            catch (IOException)
            {
                ended = true;
            }
        }
    }

    private static TcpNmeaBroadcaster CreateBroadcaster(TcpBroadcasterOptions? options = null)
        => new(options ?? new TcpBroadcasterOptions { Address = IPAddress.Loopback });

    private static async Task<TcpNmeaConnection> ConnectAsync(TcpNmeaBroadcaster broadcaster, CancellationToken cancellationToken)
    {
        TcpNmeaConnection connection = new("127.0.0.1", broadcaster.LocalEndPoint.Port);
        await connection.ConnectAsync(cancellationToken);
        return connection;
    }

    private static async Task<TcpClient> ConnectSlowClientAsync(TcpNmeaBroadcaster broadcaster, CancellationToken cancellationToken)
    {
        // A small receive window makes the client's socket back up quickly.
        TcpClient client = new() { ReceiveBufferSize = 1024 };
        await client.ConnectAsync(IPAddress.Loopback, broadcaster.LocalEndPoint.Port, cancellationToken);
        return client;
    }

    private static byte[] LargeSentence(int index)
        => Encoding.ASCII.GetBytes($"{index:D8},{new string('x', 1000)}");

    private static async Task<string> ReadStringAsync(TcpNmeaConnection connection, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(cancellationToken);
        line.HasValue.ShouldBeTrue();
        return Encoding.ASCII.GetString(line.Value.Span);
    }

    private static async Task<string> ReadExactlyAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return Encoding.ASCII.GetString(buffer);
    }

    private static async Task WaitForClientCountAsync(INmeaBroadcaster broadcaster, int expected, CancellationToken cancellationToken)
    {
        while (broadcaster.ClientCount != expected)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private CancellationTokenSource CreateTimeout()
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TestTimeout);
        return cts;
    }
}
