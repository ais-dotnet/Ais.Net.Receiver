// <copyright file="WebSocketNmeaReceiverTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net.WebSockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class WebSocketNmeaReceiverTests
{
    private static readonly Uri TestUrl = new("ws://feed.example:8080/ais");

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task GetAsync_WhenConnected_YieldsLines()
    {
        FakeNmeaConnection connection = new();
        connection.Reads.Enqueue(_ => Line("Line1"));
        connection.Reads.Enqueue(_ => Line("Line2"));
        connection.Reads.Enqueue(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        });

        await using WebSocketNmeaReceiver receiver = new(connection, TestUrl, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        List<string> result = await this.CollectAsync(receiver, 2);

        result.ShouldBe(["Line1", "Line2"]);
        connection.ConnectCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task GetAsync_EndOfStream_Reconnects()
    {
        FakeNmeaConnection connection = new();
        connection.Reads.Enqueue(_ => Line("Line1"));
        connection.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(result: null));
        connection.Reads.Enqueue(_ => Line("Line2"));

        await using WebSocketNmeaReceiver receiver = new(connection, TestUrl, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        List<string> result = await this.CollectAsync(receiver, 2);

        result.ShouldBe(["Line1", "Line2"]);
        connection.ConnectCount.ShouldBe(2);
        connection.DisposeCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [TestMethod]
    public async Task GetAsync_ConnectFailure_RetriesAndSucceeds()
    {
        FakeNmeaConnection connection = new();
        connection.Connects.Enqueue(_ => throw new WebSocketException("refused"));
        connection.Reads.Enqueue(_ => Line("Line1"));

        await using WebSocketNmeaReceiver receiver = new(connection, TestUrl, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        List<string> result = await this.CollectAsync(receiver, 1);

        result.ShouldBe(["Line1"]);
        connection.ConnectCount.ShouldBe(2);
    }

    [TestMethod]
    public async Task GetAsync_Cancellation_StopsEnumeration()
    {
        FakeNmeaConnection connection = new();
        connection.Reads.Enqueue(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        });

        await using WebSocketNmeaReceiver receiver = new(connection, TestUrl, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        int count = 0;

        Task enumeration = Task.Run(
            async () =>
            {
                await foreach (ReadOnlyMemory<byte> unused in receiver.GetAsync(cts.Token))
                {
                    count++;
                }
            },
            CancellationToken.None);

        Task completed = await Task.WhenAny(enumeration, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));

        completed.ShouldBeSameAs(enumeration);
        await enumeration;
        count.ShouldBe(0);
        connection.ConnectCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task GetAsync_ConnectionLifecycle_InvokesConnectionStateCallback()
    {
        FakeNmeaConnection connection = new();
        connection.Reads.Enqueue(_ => Line("Line1"));
        connection.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(result: null));
        connection.Reads.Enqueue(_ => Line("Line2"));

        List<bool> states = [];
        await using WebSocketNmeaReceiver receiver = new(
            connection,
            TestUrl,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(1),
            onConnectionStateChanged: state =>
            {
                lock (states)
                {
                    states.Add(state);
                }
            });

        await this.CollectAsync(receiver, 2);

        lock (states)
        {
            states.Take(3).ShouldBe([true, false, true]);
        }
    }

    [TestMethod]
    public async Task GetAsync_AgainstRealServer_YieldsSentencesAcrossReconnect()
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        int connections = 0;

        await using WebSocketTestServer server = await WebSocketTestServer.StartAsync(
            async ws =>
            {
                if (Interlocked.Increment(ref connections) == 1)
                {
                    await SendTextAsync(ws, "Sentence1");
                    await SendTextAsync(ws, "Sentence2\n");
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
                else
                {
                    await SendTextAsync(ws, "Sentence3\r\n");
                    await WebSocketTestServer.WaitForCloseAsync(ws);
                }
            },
            cts.Token);

        await using WebSocketNmeaReceiver receiver = new(server.Uri, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        List<string> result = [];
        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync(cts.Token))
        {
            result.Add(Encoding.ASCII.GetString(line.Span));
            if (result.Count == 3)
            {
                await cts.CancelAsync();
            }
        }

        result.ShouldBe(["Sentence1", "Sentence2", "Sentence3"]);
        Volatile.Read(ref connections).ShouldBe(2);
    }

    [TestMethod]
    public void Constructor_WithAllParameters_SetsProperties()
    {
        FakeNmeaConnection connection = new();
        TimeSpan retryPeriodicity = TimeSpan.FromSeconds(5);
        TimeSpan idleTimeout = TimeSpan.FromMinutes(2);

        WebSocketNmeaReceiver receiver = new(connection, TestUrl, TimeProvider.System, retryPeriodicity, 50, idleTimeout);

        receiver.Url.ShouldBe(TestUrl);
        receiver.RetryPeriodicity.ShouldBe(retryPeriodicity);
        receiver.RetryAttemptLimit.ShouldBe(50);
        receiver.IdleTimeout.ShouldBe(idleTimeout);
    }

    [TestMethod]
    public void Constructor_WithUrl_UsesDefaults()
    {
        WebSocketNmeaReceiver receiver = new(TestUrl, TimeProvider.System);

        receiver.Url.ShouldBe(TestUrl);
        receiver.RetryPeriodicity.ShouldBe(TimeSpan.FromSeconds(1));
        receiver.RetryAttemptLimit.ShouldBe(100);
        receiver.IdleTimeout.ShouldBeNull();
    }

    [TestMethod]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new WebSocketNmeaReceiver(null!, TestUrl, TimeProvider.System));
    }

    [TestMethod]
    public void Constructor_NullUrl_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new WebSocketNmeaReceiver(new FakeNmeaConnection(), null!, TimeProvider.System));
    }

    [TestMethod]
    public void Constructor_HttpUrl_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new WebSocketNmeaReceiver(new Uri("http://feed.example/"), TimeProvider.System));
    }

    private static ValueTask<ReadOnlyMemory<byte>?> Line(string text) =>
        new(Encoding.ASCII.GetBytes(text));

    private static Task SendTextAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.ASCII.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    private async Task<List<string>> CollectAsync(WebSocketNmeaReceiver receiver, int count)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        List<string> result = [];

        await foreach (ReadOnlyMemory<byte> item in receiver.GetAsync(cts.Token))
        {
            result.Add(Encoding.ASCII.GetString(item.Span));
            if (result.Count >= count)
            {
                await cts.CancelAsync();
            }
        }

        return result;
    }

    private sealed class FakeNmeaConnection : INmeaConnection
    {
        private int connectCount;
        private int disposeCount;

        public string EndpointAddress => TestUrl.Host;

        public int EndpointPort => TestUrl.Port;

        public string Transport => "tcp";

        public string? ProtocolName => "websocket";

        public NmeaSourceKind Kind => NmeaSourceKind.Live;

        public bool Connected { get; set; } = true;

        public int ConnectCount => Volatile.Read(ref this.connectCount);

        public int DisposeCount => Volatile.Read(ref this.disposeCount);

        public Queue<Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>> Reads { get; } = new();

        public Queue<Func<CancellationToken, Task>> Connects { get; } = new();

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.connectCount);
            return this.Connects.TryDequeue(out Func<CancellationToken, Task>? func) ? func(cancellationToken) : Task.CompletedTask;
        }

        public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken) =>
            this.Reads.TryDequeue(out Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>? func)
                ? func(cancellationToken)
                : WaitForeverAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref this.disposeCount);
            return ValueTask.CompletedTask;
        }

        // Once the script is exhausted, behave like a silent feed so tests end by cancellation
        // rather than by spinning through reconnects.
        private static async ValueTask<ReadOnlyMemory<byte>?> WaitForeverAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
}
