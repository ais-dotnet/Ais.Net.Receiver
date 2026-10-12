// <copyright file="WebSocketNmeaBroadcasterTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Broadcaster.Tests;

[TestClass]
public class WebSocketNmeaBroadcasterTests
{
    private const string Sentence1 = "!AIVDM,1,1,,B,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
    private const string Sentence2 = "!AIVDM,1,1,,A,15MgK45P3@G?fl0E`JbR0OwT0@MS,0*4E";
    private const string Sentence3 = "!AIVDM,1,1,,B,33aEP6PP00PBLRfMuQ5dRwvN0000,0*3C";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task StartAsync_PortZero_BindsAFreePortAndExposesTheUrl()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with { Path = "/ais" });

        await broadcaster.StartAsync(cts.Token);

        broadcaster.Url.Port.ShouldBeGreaterThan(0);
        broadcaster.Url.ShouldBe(new Uri($"ws://127.0.0.1:{broadcaster.Url.Port}/ais"));
        broadcaster.Name.ShouldBe(broadcaster.Url.ToString());
        broadcaster.ClientCount.ShouldBe(0);

        await using WebSocketNmeaConnection client = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        client.Connected.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Url_ForAllInterfaces_RendersAsZeroAddress()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with { Address = IPAddress.Any });

        await broadcaster.StartAsync(cts.Token);

        broadcaster.Url.ToString().ShouldBe($"ws://0.0.0.0:{broadcaster.Url.Port}/");
        broadcaster.Url.Port.ShouldBeGreaterThan(0);
    }

    [TestMethod]
    [DataRow(WebSocketFraming.SentencePerMessage)]
    [DataRow(WebSocketFraming.NewlineDelimitedBatch)]
    public async Task BroadcastAsync_ReachesEveryConnectedClient(WebSocketFraming framing)
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with { Framing = framing, BatchSize = 2 });
        await broadcaster.StartAsync(cts.Token);
        await using WebSocketNmeaConnection client1 = await ConnectAsync(broadcaster, cts.Token);
        await using WebSocketNmeaConnection client2 = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 2, cts.Token);

        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence2), cts.Token);

        foreach (WebSocketNmeaConnection client in new[] { client1, client2 })
        {
            (await ReadStringAsync(client, cts.Token)).ShouldBe(Sentence1);
            (await ReadStringAsync(client, cts.Token)).ShouldBe(Sentence2);
        }
    }

    [TestMethod]
    public async Task SentencePerMessage_SendsEachSentenceAsOneTextMessageWithNoTerminator()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with { Framing = WebSocketFraming.SentencePerMessage });
        await broadcaster.StartAsync(cts.Token);
        using ClientWebSocket client = await ConnectRawAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence2), cts.Token);

        (await ReceiveMessageAsync(client, cts.Token)).ShouldBe(Sentence1);
        (await ReceiveMessageAsync(client, cts.Token)).ShouldBe(Sentence2);
    }

    [TestMethod]
    [DataRow(NmeaLineEnding.Lf, "\n")]
    [DataRow(NmeaLineEnding.CrLf, "\r\n")]
    public async Task NewlineDelimitedBatch_FlushesWhenBatchSizeIsReached_WithTheConfiguredLineEnding(NmeaLineEnding lineEnding, string terminator)
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with
        {
            Framing = WebSocketFraming.NewlineDelimitedBatch,
            LineEnding = lineEnding,
            BatchSize = 3,
            BatchInterval = TimeSpan.FromSeconds(30),
        });
        await broadcaster.StartAsync(cts.Token);
        using ClientWebSocket client = await ConnectRawAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence2), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence3), cts.Token);

        string message = await ReceiveMessageAsync(client, cts.Token);
        stopwatch.Stop();

        message.ShouldBe(Sentence1 + terminator + Sentence2 + terminator + Sentence3 + terminator);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task NewlineDelimitedBatch_FlushesAPartialBatchWhenTheIntervalElapses()
    {
        TimeSpan interval = TimeSpan.FromMilliseconds(200);
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with
        {
            Framing = WebSocketFraming.NewlineDelimitedBatch,
            LineEnding = NmeaLineEnding.Lf,
            BatchSize = 100,
            BatchInterval = interval,
        });
        await broadcaster.StartAsync(cts.Token);
        using ClientWebSocket client = await ConnectRawAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence2), cts.Token);

        string message = await ReceiveMessageAsync(client, cts.Token);
        stopwatch.Stop();

        message.ShouldBe(Sentence1 + "\n" + Sentence2 + "\n");
        stopwatch.Elapsed.ShouldBeGreaterThan(interval - TimeSpan.FromMilliseconds(50));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task NonWebSocketRequest_ToThePath_Returns400()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with { Path = "/ais" });
        await broadcaster.StartAsync(cts.Token);
        using HttpClient http = new();
        Uri httpUrl = new($"http://127.0.0.1:{broadcaster.Url.Port}/ais");

        using HttpResponseMessage response = await http.GetAsync(httpUrl, cts.Token);
        using HttpResponseMessage otherPath = await http.GetAsync(new Uri(httpUrl, "/elsewhere"), cts.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        otherPath.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        broadcaster.ClientCount.ShouldBe(0);
    }

    [TestMethod]
    public async Task DisconnectAllAsync_ClosesClientsAndKeepsServing()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        await using WebSocketNmeaConnection client1 = await ConnectAsync(broadcaster, cts.Token);
        using ClientWebSocket client2 = await ConnectRawAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 2, cts.Token);

        Task disconnect = broadcaster.DisconnectAllAsync(cts.Token);

        broadcaster.ClientCount.ShouldBe(0);
        (await client1.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
        WebSocketReceiveResult result = await client2.ReceiveAsync(new byte[1024], cts.Token);
        result.MessageType.ShouldBe(WebSocketMessageType.Close);
        client2.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);

        // Acknowledge the close, as a well-behaved client does, so the broadcaster need not wait
        // out its close timeout. The receiver acknowledges as it reconnects, as after a dropped feed.
        await client2.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        await client1.ConnectAsync(cts.Token);
        await disconnect;
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);
        await broadcaster.BroadcastAsync(Encoding.ASCII.GetBytes(Sentence1), cts.Token);
        (await ReadStringAsync(client1, cts.Token)).ShouldBe(Sentence1);
    }

    [TestMethod]
    public async Task ClientThatDisconnects_IsRemoved()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        await using WebSocketNmeaConnection staying = await ConnectAsync(broadcaster, cts.Token);
        WebSocketNmeaConnection leaving = await ConnectAsync(broadcaster, cts.Token);
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
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster();

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
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);
        Uri url = broadcaster.Url;
        await using WebSocketNmeaConnection client = await ConnectAsync(broadcaster, cts.Token);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        Task stop = broadcaster.StopAsync(cts.Token);

        (await client.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();

        // Disposing acknowledges the close, so the broadcaster need not wait out its close timeout.
        await client.DisposeAsync();
        await stop;

        broadcaster.ClientCount.ShouldBe(0);
        await using WebSocketNmeaConnection late = new(url);
        await Should.ThrowAsync<WebSocketException>(late.ConnectAsync(cts.Token));
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster();
        await broadcaster.StartAsync(cts.Token);

        await broadcaster.DisposeAsync();
        await broadcaster.DisposeAsync();
        await broadcaster.StopAsync(cts.Token);

        // Never started is fine too.
        WebSocketNmeaBroadcaster unstarted = CreateBroadcaster();
        await unstarted.DisposeAsync();
        await unstarted.DisposeAsync();
    }

    [TestMethod]
    public async Task SlowClient_WithDropOldest_SkipsAheadToTheMostRecentSentencesAndStaysConnected()
    {
        const int sentenceCount = 20_000;
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with
        {
            ClientQueueCapacity = 2,
            SlowClient = SlowClientPolicy.DropOldest,
        });
        await broadcaster.StartAsync(cts.Token);
        using SocketsHttpHandler handler = CreateSlowHandler();
        using HttpMessageInvoker invoker = new(handler);
        using ClientWebSocket client = await ConnectRawAsync(broadcaster, cts.Token, invoker);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        // The client reads nothing while the whole burst is broadcast, so its socket buffers fill
        // and all but the newest queued sentences are dropped.
        for (int i = 0; i < sentenceCount; i++)
        {
            await broadcaster.BroadcastAsync(LargeSentence(i), cts.Token);
        }

        List<int> received = [];
        while (received.Count == 0 || received[^1] != sentenceCount - 1)
        {
            string message = await ReceiveMessageAsync(client, cts.Token);
            received.Add(int.Parse(message.AsSpan(0, 8), provider: null));
        }

        broadcaster.ClientCount.ShouldBe(1);
        client.State.ShouldBe(WebSocketState.Open);
        received.Count.ShouldBeLessThan(sentenceCount);
        received.ShouldBeInOrder(SortDirection.Ascending);
        received[^2].ShouldBe(sentenceCount - 2);
    }

    [TestMethod]
    public async Task SlowClient_WithDisconnect_IsClosed()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketNmeaBroadcaster broadcaster = CreateBroadcaster(Options() with
        {
            ClientQueueCapacity = 2,
            SlowClient = SlowClientPolicy.Disconnect,
        });
        await broadcaster.StartAsync(cts.Token);
        using SocketsHttpHandler handler = CreateSlowHandler();
        using HttpMessageInvoker invoker = new(handler);
        using ClientWebSocket client = await ConnectRawAsync(broadcaster, cts.Token, invoker);
        await WaitForClientCountAsync(broadcaster, 1, cts.Token);

        for (int i = 0; i < 100_000 && broadcaster.ClientCount > 0; i++)
        {
            await broadcaster.BroadcastAsync(LargeSentence(i), cts.Token);
        }

        broadcaster.ClientCount.ShouldBe(0);

        // Whatever was already in flight drains, then the connection ends.
        byte[] buffer = new byte[64 * 1024];
        bool ended = false;
        while (!ended)
        {
            try
            {
                WebSocketReceiveResult result = await client.ReceiveAsync(buffer, cts.Token);
                ended = result.MessageType == WebSocketMessageType.Close;
            }
            catch (WebSocketException)
            {
                ended = true;
            }
        }

        client.State.ShouldNotBe(WebSocketState.Open);
    }

    private static WebSocketBroadcasterOptions Options() => new() { Address = IPAddress.Loopback };

    private static WebSocketNmeaBroadcaster CreateBroadcaster(WebSocketBroadcasterOptions? options = null)
        => new(options ?? Options());

    private static async Task<WebSocketNmeaConnection> ConnectAsync(WebSocketNmeaBroadcaster broadcaster, CancellationToken cancellationToken)
    {
        WebSocketNmeaConnection connection = new(broadcaster.Url);
        await connection.ConnectAsync(cancellationToken);
        return connection;
    }

    private static async Task<ClientWebSocket> ConnectRawAsync(WebSocketNmeaBroadcaster broadcaster, CancellationToken cancellationToken, HttpMessageInvoker? invoker = null)
    {
        ClientWebSocket client = new();
        await client.ConnectAsync(broadcaster.Url, invoker, cancellationToken);
        return client;
    }

    private static SocketsHttpHandler CreateSlowHandler()
    {
        // A small receive window makes the client's socket back up quickly.
        return new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { ReceiveBufferSize = 1024, NoDelay = true };
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    private static byte[] LargeSentence(int index)
        => Encoding.ASCII.GetBytes($"{index:D8},{new string('x', 1000)}");

    private static async Task<string> ReceiveMessageAsync(ClientWebSocket client, CancellationToken cancellationToken)
    {
        using MemoryStream message = new();
        byte[] buffer = new byte[8 * 1024];
        while (true)
        {
            WebSocketReceiveResult result = await client.ReceiveAsync(buffer, cancellationToken);
            result.MessageType.ShouldBe(WebSocketMessageType.Text);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.ASCII.GetString(message.ToArray());
            }
        }
    }

    private static async Task<string> ReadStringAsync(WebSocketNmeaConnection connection, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(cancellationToken);
        line.HasValue.ShouldBeTrue();
        return Encoding.ASCII.GetString(line.Value.Span);
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
