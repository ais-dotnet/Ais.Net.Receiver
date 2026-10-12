// <copyright file="WebSocketNmeaConnectionTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Logging;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class WebSocketNmeaConnectionTests
{
    private const string Sentence1 = "!AIVDM,1,1,,A,13aEOK?P00PD2wVMdLDRhgvL289?,0*26";
    private const string Sentence2 = "!AIVDM,1,1,,B,13aEOK?P00PD2wVMdLDRhgvL289?,0*25";
    private const string Sentence3 = "!AIVDM,1,1,,A,B52K>;h00Fc>jpUlNV@ikwpUoP06,0*4C";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ConnectAsync_ConnectsAndReportsConnected()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketTestServer server = await WebSocketTestServer.StartAsync(WebSocketTestServer.WaitForCloseAsync, cts.Token);
        await using WebSocketNmeaConnection connection = new(server.Uri);

        connection.Connected.ShouldBeFalse();
        await connection.ConnectAsync(cts.Token);

        connection.Connected.ShouldBeTrue();
        connection.EndpointAddress.ShouldBe("127.0.0.1");
        connection.EndpointPort.ShouldBe(server.Uri.Port);
        connection.Transport.ShouldBe("tcp");
        connection.ProtocolName.ShouldBe("websocket");
    }

    [TestMethod]
    public async Task ReadLineAsync_OneSentencePerMessageWithoutNewline_YieldsEachSentence()
    {
        List<string?> lines = await this.ReadLinesAsync(
            2,
            async ws =>
            {
                await SendTextAsync(ws, Sentence1);
                await SendTextAsync(ws, Sentence2);
            });

        lines.ShouldBe([Sentence1, Sentence2]);
    }

    [TestMethod]
    public async Task ReadLineAsync_MessageWithThreeNewlineSeparatedSentences_YieldsThreeLines()
    {
        List<string?> lines = await this.ReadLinesAsync(
            3,
            ws => SendTextAsync(ws, $"{Sentence1}\n{Sentence2}\n{Sentence3}"));

        lines.ShouldBe([Sentence1, Sentence2, Sentence3]);
    }

    [TestMethod]
    public async Task ReadLineAsync_SentenceSplitAcrossFramesOfOneMessage_YieldsOneLine()
    {
        List<string?> lines = await this.ReadLinesAsync(
            2,
            async ws =>
            {
                await ws.SendAsync(Encoding.ASCII.GetBytes(Sentence1[..20]), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
                await ws.SendAsync(Encoding.ASCII.GetBytes(Sentence1[20..]), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                await SendTextAsync(ws, Sentence2);
            });

        lines.ShouldBe([Sentence1, Sentence2]);
    }

    [TestMethod]
    public async Task ReadLineAsync_CrLfLineEndings_AreTrimmed()
    {
        List<string?> lines = await this.ReadLinesAsync(
            2,
            ws => SendTextAsync(ws, $"{Sentence1}\r\n{Sentence2}\r\n"));

        lines.ShouldBe([Sentence1, Sentence2]);
    }

    [TestMethod]
    public async Task ReadLineAsync_TrailingNewlineAtEndOfMessage_DoesNotProduceEmptyLine()
    {
        List<string?> lines = await this.ReadLinesAsync(
            2,
            async ws =>
            {
                await SendTextAsync(ws, $"{Sentence1}\n");
                await SendTextAsync(ws, $"{Sentence2}\n");
            });

        lines.ShouldBe([Sentence1, Sentence2]);
    }

    [TestMethod]
    public async Task ReadLineAsync_BinaryMessages_AreAccepted()
    {
        List<string?> lines = await this.ReadLinesAsync(
            2,
            async ws =>
            {
                await ws.SendAsync(Encoding.ASCII.GetBytes(Sentence1), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
                await ws.SendAsync(Encoding.ASCII.GetBytes($"{Sentence2}\n"), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
            });

        lines.ShouldBe([Sentence1, Sentence2]);
    }

    [TestMethod]
    public async Task ReadLineAsync_OverLongMessage_IsDiscardedAndNextSentenceArrives()
    {
        List<string?> lines = await this.ReadLinesAsync(
            1,
            async ws =>
            {
                await SendTextAsync(ws, new string('A', 10_000));
                await SendTextAsync(ws, Sentence1);
            });

        lines.ShouldBe([Sentence1]);
    }

    [TestMethod]
    public async Task Kind_IsLive()
    {
        await using WebSocketNmeaConnection connection = new(new Uri("ws://127.0.0.1:1/"));

        connection.Kind.ShouldBe(NmeaSourceKind.Live);
    }

    [TestMethod]
    public async Task ReadLineAsync_MessageAtLineLengthCap_IsDeliveredAndOneByteOver_IsDiscarded()
    {
        string atCap = new('A', NmeaLineReader.DefaultMaxLineLength);
        string overCap = new('B', NmeaLineReader.DefaultMaxLineLength + 1);

        List<string?> lines = await this.ReadLinesAsync(
            2,
            async ws =>
            {
                await SendTextAsync(ws, $"{overCap}\n");
                await SendTextAsync(ws, $"{atCap}\n");
                await SendTextAsync(ws, Sentence1);
            });

        lines.ShouldBe([atCap, Sentence1]);
    }

    [TestMethod]
    public async Task ReadLineAsync_UnterminatedMessageFollowedByNewSentence_YieldsSeparateLines()
    {
        string fragment = Sentence1[..20];

        List<string?> lines = await this.ReadLinesAsync(
            2,
            async ws =>
            {
                await SendTextAsync(ws, fragment);
                await SendTextAsync(ws, $"{Sentence2}\n");
            });

        lines.ShouldBe([fragment, Sentence2]);
    }

    [TestMethod]
    public async Task ReadLineAsync_ZeroLengthMessage_ProducesNoLine()
    {
        List<string?> lines = await this.ReadLinesAsync(
            1,
            async ws =>
            {
                await ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                await SendTextAsync(ws, Sentence1);
            });

        lines.ShouldBe([Sentence1]);
    }

    [TestMethod]
    public async Task ReadLineAsync_PeerClose_ReturnsNullAndDisconnects()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketTestServer server = await WebSocketTestServer.StartAsync(
            async ws =>
            {
                await SendTextAsync(ws, Sentence1);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            },
            cts.Token);
        await using WebSocketNmeaConnection connection = new(server.Uri);
        await connection.ConnectAsync(cts.Token);

        ReadOnlyMemory<byte>? first = await connection.ReadLineAsync(cts.Token);
        ReadOnlyMemory<byte>? second = await connection.ReadLineAsync(cts.Token);

        first.ShouldNotBeNull();
        Encoding.ASCII.GetString(first.Value.Span).ShouldBe(Sentence1);
        second.ShouldBeNull();
        connection.Connected.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ReadLineAsync_PeerClose_LogsClosedByPeerWithStatus()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketTestServer server = await WebSocketTestServer.StartAsync(
            ws => ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None),
            cts.Token);
        CapturingLogger logger = new();
        await using WebSocketNmeaConnection connection = new(server.Uri, logger);
        await connection.ConnectAsync(cts.Token);

        ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(cts.Token);

        line.ShouldBeNull();
        logger.Entries.ShouldContain(e => e.EventId == 4035 && e.Message.Contains("NormalClosure") && e.Message.Contains("bye"));
        logger.Entries.ShouldNotContain(e => e.EventId == 4036);
    }

    [TestMethod]
    public async Task ReadLineAsync_WhenNotConnected_ReturnsNull()
    {
        await using WebSocketNmeaConnection connection = new(new Uri("ws://127.0.0.1:1/"));

        ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(this.TestContext.CancellationToken);

        line.ShouldBeNull();
    }

    [TestMethod]
    public async Task ConnectAsync_ClosedPort_Throws()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        int port = GetUnusedPort();
        await using WebSocketNmeaConnection connection = new(new Uri($"ws://127.0.0.1:{port}/"));

        await Should.ThrowAsync<WebSocketException>(() => connection.ConnectAsync(cts.Token));

        connection.Connected.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ConnectAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        await using WebSocketNmeaConnection connection = new(new Uri("ws://127.0.0.1:1/"));
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Exception? caught = null;
        try
        {
            await connection.ConnectAsync(cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.ShouldBeAssignableTo<OperationCanceledException>();
        connection.Connected.ShouldBeFalse();
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketTestServer server = await WebSocketTestServer.StartAsync(WebSocketTestServer.WaitForCloseAsync, cts.Token);
        WebSocketNmeaConnection connection = new(server.Uri);
        await connection.ConnectAsync(cts.Token);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        connection.Connected.ShouldBeFalse();
        (await connection.ReadLineAsync(cts.Token)).ShouldBeNull();
    }

    [TestMethod]
    public void Constructor_HttpUri_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new WebSocketNmeaConnection(new Uri("http://127.0.0.1/")));
    }

    [TestMethod]
    public void Constructor_RelativeUri_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new WebSocketNmeaConnection(new Uri("/feed", UriKind.Relative)));
    }

    private static Task SendTextAsync(WebSocket socket, string text) =>
        socket.SendAsync(Encoding.ASCII.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    private static int GetUnusedPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private CancellationTokenSource CreateTimeout()
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        return cts;
    }

    private async Task<List<string?>> ReadLinesAsync(int count, Func<WebSocket, Task> send)
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        await using WebSocketTestServer server = await WebSocketTestServer.StartAsync(
            async ws =>
            {
                await send(ws);
                await WebSocketTestServer.WaitForCloseAsync(ws);
            },
            cts.Token);
        await using WebSocketNmeaConnection connection = new(server.Uri);
        await connection.ConnectAsync(cts.Token);

        List<string?> lines = [];
        for (int i = 0; i < count; i++)
        {
            ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(cts.Token);
            lines.Add(line is { } l ? Encoding.ASCII.GetString(l.Span) : null);
        }

        return lines;
    }

    /// <summary>Records the event id and formatted message of each log entry.</summary>
    private sealed class CapturingLogger : ILogger<WebSocketNmeaConnection>
    {
        private readonly List<(int EventId, string Message)> entries = [];

        public IReadOnlyList<(int EventId, string Message)> Entries => this.entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (this.entries)
            {
                this.entries.Add((eventId.Id, formatter(state, exception)));
            }
        }
    }
}
