// <copyright file="EndToEndTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;

using Ais.Net.Receiver.Parser;
using Ais.Net.Receiver.Receiver;

using Shouldly;

using Spectre.IO;
using Spectre.IO.Testing;

namespace Ais.Net.Broadcaster.Tests;

/// <summary>
/// The broadcaster wired together as the CLI wires it - a file source, the pipeline and both real
/// listeners - and consumed by the receiver's own TCP and WebSocket connections.
/// </summary>
[TestClass]
public class EndToEndTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] Sentences =
    [
        "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C",
        "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24",
        "!AIVDM,1,1,,A,13aEOK?P00PD2wVMdLDRhgvL289?,0*26",
        "!AIVDM,1,1,,B,13aEOK?P00PD2wVMdLDRhgvL289?,0*25",
        "!AIVDM,2,1,,A,55P5TL01VIaAL@7WKO4806<D18E8222222222216C8888888888888888800,0*33",
        "!AIVDM,2,2,,A,00000000000,2*25",
        "!AIVDM,1,1,,A,B52K>;h00Fc>jpUlNV@ikwpUoP06,0*4C",
        "!AIVDM,1,1,,A,H42O55i18tMET00000000000000,2*6D",
        "!AIVDM,1,1,,A,KCQ9r=hrFUnH7P00,0*41",
        "!AIVDM,1,1,,B,C5N3SRgPEnJGEBT>NhWAwwo862PaLELTBJ:V00000000S0D:R220,0*0B",
    ];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FileReplay_ReachesTcpAndWebSocketReceiversByteForByte_AndCancellationStopsBothSinks()
    {
        using CancellationTokenSource timeout = this.CreateTimeout();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        FakeFileSystem fileSystem = new(FakeEnvironment.CreateLinuxEnvironment());
        FilePath path = new("/recordings/feed.nm4");
        fileSystem.CreateFile(path).SetTextContent(string.Join("\r\n", Sentences) + "\r\n");

        await using TcpNmeaBroadcaster tcp = new(new TcpBroadcasterOptions { Address = IPAddress.Loopback });
        await using WebSocketNmeaBroadcaster webSocket = new(new WebSocketBroadcasterOptions { Address = IPAddress.Loopback });

        // The pipeline starts the sinks itself and broadcasts to whoever is connected, so the
        // source holds the first sentence back until both receivers are listening.
        await using GatedSource source = new(new NmeaReceiver(new FileNmeaConnection(fileSystem, path), TimeProvider.System));
        BroadcastPipeline pipeline = new(source, [tcp, webSocket], new BroadcastPipelineOptions(), TimeProvider.System);

        Task run = Task.Run(() => pipeline.RunAsync(stop.Token), timeout.Token);

        await source.SinksStarted.WaitAsync(timeout.Token);

        int tcpPort = tcp.LocalEndPoint.Port;
        Uri webSocketUrl = webSocket.Url;

        await using TcpNmeaConnection tcpClient = new("127.0.0.1", tcpPort);
        await using WebSocketNmeaConnection webSocketClient = new(webSocketUrl);
        await Task.WhenAll(tcpClient.ConnectAsync(timeout.Token), webSocketClient.ConnectAsync(timeout.Token));
        await WaitForClientCountAsync(tcp, 1, timeout.Token);
        await WaitForClientCountAsync(webSocket, 1, timeout.Token);

        source.Release();

        Task<List<string>> tcpLines = ReadLinesAsync(tcpClient, Sentences.Length, timeout.Token);
        Task<List<string>> webSocketLines = ReadLinesAsync(webSocketClient, Sentences.Length, timeout.Token);

        (await tcpLines).ShouldBe(Sentences);
        (await webSocketLines).ShouldBe(Sentences);
        pipeline.SentencesBroadcast.ShouldBe(Sentences.Length);

        await stop.CancelAsync();
        await run.WaitAsync(timeout.Token);

        // Both sinks have stopped: their clients were closed and nothing listens on their ports.
        tcp.ClientCount.ShouldBe(0);
        webSocket.ClientCount.ShouldBe(0);
        (await tcpClient.ReadLineAsync(timeout.Token)).HasValue.ShouldBeFalse();

        // Asked of the operating system rather than probed with a connection, because a refused
        // connection to loopback takes seconds to fail on Windows.
        int[] listening = [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port)];
        listening.ShouldNotContain(tcpPort);
        listening.ShouldNotContain(webSocketUrl.Port);
    }

    [TestMethod]
    public void TagBlockChecksum_RoundTripsThroughTheReceiversParser()
    {
        string line = WithTagBlock(2573010, 1760270400, Sentences[0]);

        line.ShouldBe(@"\s:2573010,c:1760270400*0F\" + Sentences[0]);

        (int stationId, long timestamp) = Encoding.ASCII.GetBytes(line).AsSpan().ParseNmeaBlockTags();
        stationId.ShouldBe(2573010);
        timestamp.ShouldBe(1760270400);
        HasValidTagBlockChecksum(line).ShouldBeTrue();
    }

    [TestMethod]
    public void SampleRecording_EveryLineHasAValidTagBlockAndTimestampsNeverGoBackwards()
    {
        // Copied into the test output by the project file, so this does not depend on the working
        // directory the test runner happens to use.
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "sample.nm4");
        System.IO.File.Exists(path).ShouldBeTrue($"Expected the sample recording at {path}");

        byte[] content = System.IO.File.ReadAllBytes(path);
        string text = Encoding.ASCII.GetString(content);
        text.ShouldEndWith("\r\n");
        text.Replace("\r\n", string.Empty, StringComparison.Ordinal).ShouldNotContain('\n');

        string[] lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBeGreaterThan(10);

        long previous = 0;
        foreach (string line in lines)
        {
            (int stationId, long timestamp) = Encoding.ASCII.GetBytes(line).AsSpan().ParseNmeaBlockTags();
            timestamp.ShouldBeGreaterThan(0, line);
            timestamp.ShouldBeGreaterThanOrEqualTo(previous, line);
            stationId.ShouldBe(2573010, line);
            HasValidTagBlockChecksum(line).ShouldBeTrue(line);
            line[(line.IndexOf('\\', 1) + 1)..].ShouldStartWith("!AIVDM,");
            previous = timestamp;
        }
    }

    [TestMethod]
    public void RealRecording_EveryLineIsTaggedAndMultiPartPairsAreWhole()
    {
        // A contiguous slice of a real capture, exactly as the Norwegian feed sent it: many base
        // stations, each stamping its own clock, so timestamps may step back by a second or so.
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "kystverket-20260511T00.nm4");
        if (!System.IO.File.Exists(path))
        {
            // The real capture is optional (it is not committed by default); the synthetic sample is
            // covered by the test above.
            Assert.Inconclusive($"The recording is not present at {path}.");
        }

        string text = Encoding.ASCII.GetString(System.IO.File.ReadAllBytes(path));
        text.ShouldEndWith("\r\n");
        text.Replace("\r\n", string.Empty, StringComparison.Ordinal).ShouldNotContain('\n');

        string[] lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2000);

        long newest = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            (int stationId, long timestamp) = Encoding.ASCII.GetBytes(line).AsSpan().ParseNmeaBlockTags();
            stationId.ShouldBeGreaterThan(0, line);
            timestamp.ShouldBeGreaterThan(0, line);
            timestamp.ShouldBeGreaterThanOrEqualTo(newest - 5, line);
            newest = Math.Max(newest, timestamp);

            string sentence = line[(line.IndexOf('\\', 1) + 1)..];
            // The feed uses several talker ids (BS, B1, B2), so only the sentence shape is checked.
            sentence.ShouldMatch("^![A-Z0-9]{2}VD[MO],");

            // Match the fragment-count and fragment-index fields, not any ",2,1," inside a later field.
            if (System.Text.RegularExpressions.Regex.IsMatch(sentence, "^![A-Z0-9]{2}VD[MO],2,1,"))
            {
                // The slice must not split a multi-part message.
                i.ShouldBeLessThan(lines.Length - 1, line);
                lines[i + 1][(lines[i + 1].IndexOf('\\', 1) + 1)..].ShouldMatch("^![A-Z0-9]{2}VD[MO],2,2,", line);
            }
        }
    }

    private static string WithTagBlock(int stationId, long unixSeconds, string sentence)
    {
        string tags = $"s:{stationId},c:{unixSeconds}";
        return $@"\{tags}*{TagBlockChecksum(tags):X2}\{sentence}";
    }

    private static int TagBlockChecksum(string tags)
    {
        // NMEA 4.10: the XOR of every character between the opening backslash and the asterisk.
        int checksum = 0;
        foreach (char c in tags)
        {
            checksum ^= c;
        }

        return checksum;
    }

    private static bool HasValidTagBlockChecksum(string line)
    {
        int end = line.IndexOf('\\', 1);
        int star = line.LastIndexOf('*', end);
        return line[0] == '\\' && star > 0 &&
            line[(star + 1)..end] == TagBlockChecksum(line[1..star]).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<List<string>> ReadLinesAsync(INmeaConnection connection, int count, CancellationToken cancellationToken)
    {
        List<string> lines = [];
        while (lines.Count < count)
        {
            ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(cancellationToken);
            line.HasValue.ShouldBeTrue($"The connection closed after {lines.Count} of {count} lines.");
            lines.Add(Encoding.ASCII.GetString(line.Value.Span));
        }

        return lines;
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

    /// <summary>
    /// Wraps a finite source so the test controls when it starts and keeps it open afterwards: it
    /// signals once the pipeline has started the sinks and asked for sentences, waits to be
    /// released, yields the inner source, and then idles until cancelled so that the run ends by
    /// cancellation rather than by the source running out.
    /// </summary>
    private sealed class GatedSource(INmeaReceiver inner) : INmeaReceiver
    {
        private readonly TaskCompletionSource sinksStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SinksStarted => this.sinksStarted.Task;

        public void Release() => this.released.TrySetResult();

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            this.sinksStarted.TrySetResult();
            await this.released.Task.WaitAsync(cancellationToken);

            await foreach (ReadOnlyMemory<byte> line in inner.GetAsync(cancellationToken))
            {
                yield return line;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
