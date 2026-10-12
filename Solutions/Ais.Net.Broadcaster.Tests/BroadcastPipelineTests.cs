// <copyright file="BroadcastPipelineTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;

using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Spectre.IO;
using Spectre.IO.Testing;

namespace Ais.Net.Broadcaster.Tests;

[TestClass]
public sealed class BroadcastPipelineTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    // Long enough for a pipeline that is wrongly not waiting to have sent its next sentence.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(50);

    private readonly FilePath testFilePath = new("/test/recording.nmea");
    private FakeFileSystem fileSystem = null!;
    private FakeTimeProvider timeProvider = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        this.fileSystem = new FakeFileSystem(FakeEnvironment.CreateLinuxEnvironment());
        this.timeProvider = new FakeTimeProvider();
    }

    [TestMethod]
    public void Constructor_InvalidArguments_Throw()
    {
        NmeaReceiver source = this.CreateSource("A");
        FakeNmeaBroadcaster[] sinks = [new()];
        BroadcastPipelineOptions options = new();

        Should.Throw<ArgumentNullException>(() => new BroadcastPipeline(null!, sinks, options, this.timeProvider));
        Should.Throw<ArgumentNullException>(() => new BroadcastPipeline(source, null!, options, this.timeProvider));
        Should.Throw<ArgumentNullException>(() => new BroadcastPipeline(source, sinks, null!, this.timeProvider));
        Should.Throw<ArgumentNullException>(() => new BroadcastPipeline(source, sinks, options, null!));
        Should.Throw<ArgumentException>(() => new BroadcastPipeline(source, [], options, this.timeProvider));
        Should.Throw<ArgumentOutOfRangeException>(() => new BroadcastPipeline(source, sinks, options with { Pacing = PacingMode.Fixed, SentencesPerSecond = 0 }, this.timeProvider));
        Should.Throw<ArgumentOutOfRangeException>(() => new BroadcastPipeline(source, sinks, options with { Pacing = PacingMode.TagBlockTimestamps, SpeedFactor = 0 }, this.timeProvider));
        Should.Throw<ArgumentOutOfRangeException>(() => new BroadcastPipeline(source, sinks, options with { Faults = new() { MalformedEveryN = 0 } }, this.timeProvider));
    }

    [TestMethod]
    public async Task RunAsync_NoPacing_DeliversEverySentenceToEverySinkInOrder()
    {
        string[] lines = ["!AIVDM,1,1,,A,one,0*00", "!AIVDM,1,1,,A,two,0*00", "!AIVDM,1,1,,A,three,0*00", "!AIVDM,1,1,,A,four,0*00"];
        FakeNmeaBroadcaster first = new("fake://first");
        FakeNmeaBroadcaster second = new("fake://second");
        BroadcastPipeline pipeline = new(this.CreateSource(lines), [first, second], new BroadcastPipelineOptions(), this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        await Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        foreach (FakeNmeaBroadcaster sink in new[] { first, second })
        {
            sink.BroadcastText.ShouldBe(lines);
            sink.Events.ShouldBe(["start", "broadcast", "broadcast", "broadcast", "broadcast", "stop"]);
        }

        pipeline.SentencesBroadcast.ShouldBe(4);
        pipeline.FaultsInjected.ShouldBe(0);
        pipeline.Loops.ShouldBe(0);
    }

    [TestMethod]
    public async Task RunAsync_Loop_ReEnumeratesTheSourceAndCountsLoops()
    {
        string[] lines = ["L1", "L2", "L3"];
        FakeNmeaBroadcaster sink = new();
        BroadcastPipeline pipeline = new(this.CreateSource(lines), [sink], new BroadcastPipelineOptions { Loop = true }, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        Task run = Task.Run(() => pipeline.RunAsync(stop.Token), timeout.Token);
        await WaitUntilAsync(() => pipeline.Loops >= 3, timeout.Token);
        await stop.CancelAsync();
        await run;

        timeout.IsCancellationRequested.ShouldBeFalse();
        pipeline.Loops.ShouldBeGreaterThanOrEqualTo(3);
        IReadOnlyList<string> sent = sink.BroadcastText;
        sent.Count.ShouldBeGreaterThanOrEqualTo(9);
        for (int i = 0; i < sent.Count; i++)
        {
            sent[i].ShouldBe(lines[i % lines.Length]);
        }

        sink.StopCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task RunAsync_FixedPacing_SendsNoSentenceBeforeItsSlot()
    {
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Pacing = PacingMode.Fixed, SentencesPerSecond = 10 };
        BroadcastPipeline pipeline = new(this.CreateSource("L1", "L2", "L3"), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        Task run = Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        // The first sentence is due immediately.
        await WaitUntilAsync(() => sink.BroadcastCount == 1, timeout.Token);
        await this.ShouldStayAsync(sink, 1, timeout.Token);

        this.timeProvider.Advance(TimeSpan.FromMilliseconds(99));
        await this.ShouldStayAsync(sink, 1, timeout.Token);

        this.timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntilAsync(() => sink.BroadcastCount == 2, timeout.Token);
        await this.ShouldStayAsync(sink, 2, timeout.Token);

        this.timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await run;

        sink.BroadcastText.ShouldBe(["L1", "L2", "L3"]);
        sink.StopCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task RunAsync_TagBlockPacing_HonoursGapsClampsAndSendsUntimestampedImmediately()
    {
        string[] lines =
        [
            @"\s:2573010,c:1000*00\!BSVDM,1,1,,B,first,0*00",
            @"\s:2573010,c:1002*00\!BSVDM,1,1,,B,second,0*00",
            "!AIVDM,1,1,,A,untimestamped,0*00",
            @"\s:2573010,c:1100*00\!BSVDM,1,1,,B,afterhole,0*00",
        ];
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Pacing = PacingMode.TagBlockTimestamps, MaxTagBlockGap = TimeSpan.FromSeconds(5) };
        BroadcastPipeline pipeline = new(this.CreateSource(lines), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        Task run = Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        // First timestamped sentence: immediate.
        await WaitUntilAsync(() => sink.BroadcastCount == 1, timeout.Token);
        await this.ShouldStayAsync(sink, 1, timeout.Token);

        // 2 second gap to the second.
        this.timeProvider.Advance(TimeSpan.FromMilliseconds(1999));
        await this.ShouldStayAsync(sink, 1, timeout.Token);
        this.timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        // The second, then the untimestamped one straight after it.
        await WaitUntilAsync(() => sink.BroadcastCount == 3, timeout.Token);

        // A 98 second hole is clamped to MaxTagBlockGap.
        await this.ShouldStayAsync(sink, 3, timeout.Token);
        this.timeProvider.Advance(TimeSpan.FromMilliseconds(4999));
        await this.ShouldStayAsync(sink, 3, timeout.Token);
        this.timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        await run;
        sink.BroadcastText.ShouldBe(lines);
    }

    [TestMethod]
    public async Task RunAsync_TagBlockPacing_AppliesSpeedFactor()
    {
        string[] lines =
        [
            @"\s:2573010,c:1787234980*00\!BSVDM,1,1,,B,first,0*00",
            @"\s:2573010,c:1787234984*00\!BSVDM,1,1,,B,second,0*00",
        ];
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Pacing = PacingMode.TagBlockTimestamps, SpeedFactor = 2 };
        BroadcastPipeline pipeline = new(this.CreateSource(lines), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        Task run = Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        await WaitUntilAsync(() => sink.BroadcastCount == 1, timeout.Token);

        // A 4 second gap at double speed is 2 seconds.
        this.timeProvider.Advance(TimeSpan.FromMilliseconds(1999));
        await this.ShouldStayAsync(sink, 1, timeout.Token);
        this.timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        await run;
        sink.BroadcastText.ShouldBe(lines);
    }

    [TestMethod]
    public async Task RunAsync_TagBlockPacingWithLoop_ResetsTheBaselineOnRestart()
    {
        // Within a pass the second sentence goes back in time (gap clamped to zero). Were the
        // baseline (c:1000) carried over the loop, the restarted first sentence (c:1002) would
        // wait 2 seconds; reset, it is sent immediately.
        string[] lines =
        [
            @"\s:1,c:1002*00\!AIVDM,1,1,,A,later,0*00",
            @"\s:1,c:1000*00\!AIVDM,1,1,,A,earlier,0*00",
        ];
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Pacing = PacingMode.TagBlockTimestamps, Loop = true };
        BroadcastPipeline pipeline = new(this.CreateSource(lines), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        Task run = Task.Run(() => pipeline.RunAsync(stop.Token), timeout.Token);
        await WaitUntilAsync(() => sink.BroadcastCount >= 3, timeout.Token);
        await stop.CancelAsync();
        await run;

        pipeline.Loops.ShouldBeGreaterThanOrEqualTo(1);
        sink.BroadcastText[2].ShouldBe(lines[0]);
    }

    [TestMethod]
    public async Task RunAsync_DisconnectEvery_DisconnectsEverySinkWhenTheIntervalElapses()
    {
        FakeNmeaBroadcaster first = new("fake://first");
        FakeNmeaBroadcaster second = new("fake://second");
        BroadcastPipelineOptions options = new()
        {
            Pacing = PacingMode.Fixed,
            SentencesPerSecond = 0.1, // one sentence every 10 seconds
            Faults = new() { DisconnectEvery = TimeSpan.FromSeconds(10) },
        };
        BroadcastPipeline pipeline = new(this.CreateSource("L1", "L2", "L3"), [first, second], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        Task run = Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        await WaitUntilAsync(() => first.BroadcastCount == 1, timeout.Token);
        first.DisconnectCount.ShouldBe(0);

        this.timeProvider.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => first.BroadcastCount == 2, timeout.Token);
        first.DisconnectCount.ShouldBe(1);
        second.DisconnectCount.ShouldBe(1);

        this.timeProvider.Advance(TimeSpan.FromSeconds(10));
        await run;

        foreach (FakeNmeaBroadcaster sink in new[] { first, second })
        {
            sink.Events.ShouldBe(["start", "broadcast", "disconnect", "broadcast", "disconnect", "broadcast", "stop"]);
            sink.BroadcastText.ShouldBe(["L1", "L2", "L3"]);
        }

        pipeline.FaultsInjected.ShouldBe(2);
    }

    [TestMethod]
    public async Task RunAsync_SilenceEvery_PausesForTheDurationThenResumesWithoutDisconnecting()
    {
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new()
        {
            Pacing = PacingMode.Fixed,
            SentencesPerSecond = 0.1, // one sentence every 10 seconds
            Faults = new() { SilenceEvery = TimeSpan.FromSeconds(10), SilenceDuration = TimeSpan.FromSeconds(30) },
        };
        BroadcastPipeline pipeline = new(this.CreateSource("L1", "L2", "L3"), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        Task run = Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        await WaitUntilAsync(() => sink.BroadcastCount == 1, timeout.Token);

        // L2's slot arrives, but the silence interval has also elapsed: 30 seconds of nothing.
        this.timeProvider.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => pipeline.FaultsInjected == 1, timeout.Token);
        await this.ShouldStayAsync(sink, 1, timeout.Token);
        this.timeProvider.Advance(TimeSpan.FromSeconds(29));
        await this.ShouldStayAsync(sink, 1, timeout.Token);

        // Silence over: L2 goes at once, and the fixed schedule resumes from there.
        this.timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => sink.BroadcastCount == 2, timeout.Token);
        await this.ShouldStayAsync(sink, 2, timeout.Token);

        this.timeProvider.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => pipeline.FaultsInjected == 2, timeout.Token);
        this.timeProvider.Advance(TimeSpan.FromSeconds(30));
        await run;

        sink.BroadcastText.ShouldBe(["L1", "L2", "L3"]);
        sink.DisconnectCount.ShouldBe(0);
        pipeline.FaultsInjected.ShouldBe(2);
    }

    [TestMethod]
    public async Task RunAsync_MalformedEveryN_ReplacesEveryNthSentence()
    {
        string[] lines = ["L1", "L2", "L3", "L4", "L5", "L6", "L7"];
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Faults = new() { MalformedEveryN = 3 } };
        BroadcastPipeline pipeline = new(this.CreateSource(lines), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        await Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        string malformed = Encoding.ASCII.GetString(BroadcastPipeline.MalformedLine.Span);
        sink.BroadcastText.ShouldBe(["L1", "L2", malformed, "L4", "L5", malformed, "L7"]);
        pipeline.FaultsInjected.ShouldBe(2);
        pipeline.SentencesBroadcast.ShouldBe(7);
    }

    [TestMethod]
    public void MalformedLine_HasAChecksumThatDoesNotMatch()
    {
        string line = Encoding.ASCII.GetString(BroadcastPipeline.MalformedLine.Span);
        int star = line.LastIndexOf('*');
        byte computed = 0;
        foreach (char c in line.AsSpan(1, star - 1))
        {
            computed ^= (byte)c;
        }

        Convert.ToByte(line[(star + 1)..], 16).ShouldNotBe(computed);
    }

    [TestMethod]
    public async Task RunAsync_OverLongEveryN_ReplacesEveryNthSentenceWithALineOverTheCap()
    {
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Faults = new() { OverLongEveryN = 2 } };
        BroadcastPipeline pipeline = new(this.CreateSource("L1", "L2", "L3", "L4"), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        await Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token);

        IReadOnlyList<byte[]> sent = sink.Broadcasts;
        sent.Count.ShouldBe(4);
        Encoding.ASCII.GetString(sent[0]).ShouldBe("L1");
        Encoding.ASCII.GetString(sent[2]).ShouldBe("L3");
        foreach (byte[] overLong in new[] { sent[1], sent[3] })
        {
            overLong.Length.ShouldBe(9000);
            overLong.ShouldAllBe(b => b == (byte)'A');
        }

        pipeline.FaultsInjected.ShouldBe(2);
    }

    [TestMethod]
    public async Task RunAsync_Cancelled_EndsQuietlyAndStopsSinks()
    {
        FakeNmeaBroadcaster sink = new();
        BroadcastPipelineOptions options = new() { Pacing = PacingMode.Fixed, SentencesPerSecond = 1 };
        BroadcastPipeline pipeline = new(this.CreateSource("L1", "L2", "L3"), [sink], options, this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        Task run = Task.Run(() => pipeline.RunAsync(stop.Token), timeout.Token);
        await WaitUntilAsync(() => sink.BroadcastCount == 1, timeout.Token);

        // Cancel while the pipeline waits for L2's slot.
        await stop.CancelAsync();
        await run;

        timeout.IsCancellationRequested.ShouldBeFalse();
        sink.Events.ShouldBe(["start", "broadcast", "stop"]);
    }

    [TestMethod]
    public async Task RunAsync_SourceFails_StopsSinksAndPropagates()
    {
        // No file at the path: the finite source fails to connect.
        FakeNmeaBroadcaster sink = new();
        NmeaReceiver source = new(new FileNmeaConnection(this.fileSystem, this.testFilePath), this.timeProvider);
        BroadcastPipeline pipeline = new(source, [sink], new BroadcastPipelineOptions(), this.timeProvider);
        using CancellationTokenSource timeout = this.CreateTimeout();

        Exception ex = await Should.ThrowAsync<Exception>(() => Task.Run(() => pipeline.RunAsync(timeout.Token), timeout.Token));

        ex.ShouldNotBeAssignableTo<OperationCanceledException>();
        sink.Events.ShouldBe(["start", "stop"]);
    }

    [TestMethod]
    public void SinkClientCount_SumsTheSinks()
    {
        FakeNmeaBroadcaster first = new() { ClientCount = 2 };
        FakeNmeaBroadcaster second = new() { ClientCount = 3 };
        BroadcastPipeline pipeline = new(this.CreateSource("L1"), [first, second], new BroadcastPipelineOptions(), this.timeProvider);

        pipeline.SinkClientCount.ShouldBe(5);
        second.ClientCount = 0;
        pipeline.SinkClientCount.ShouldBe(2);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(1, cancellationToken);
        }
    }

    private async Task ShouldStayAsync(FakeNmeaBroadcaster sink, int expectedCount, CancellationToken cancellationToken)
    {
        await Task.Delay(SettleTime, cancellationToken);
        sink.BroadcastCount.ShouldBe(expectedCount, $"at fake time {this.timeProvider.GetUtcNow():O}");
    }

    private NmeaReceiver CreateSource(params string[] lines)
    {
        this.fileSystem.CreateFile(this.testFilePath).SetTextContent(string.Join("\n", lines) + "\n");
        return new NmeaReceiver(new FileNmeaConnection(this.fileSystem, this.testFilePath), this.timeProvider);
    }

    private CancellationTokenSource CreateTimeout()
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TestTimeout);
        return cts;
    }
}
