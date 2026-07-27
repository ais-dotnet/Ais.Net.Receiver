// <copyright file="ReceiverEndToEndTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Hosting;
using Ais.Net.Receiver.Parser;
using Ais.Net.Receiver.Receiver;
using Ais.Net.Receiver.Storage;
using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Spectre.IO;
using Spectre.IO.Testing;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// End-to-end tests covering the whole receive-decode-persist path against a real Azurite blob
/// endpoint: NMEA sentences enter through a receiver, are decoded and tagged by
/// <see cref="ReceiverHost"/>, are batched by the production storage wiring in
/// <see cref="ReceiverPipeline"/>, and land in blob storage.
/// </summary>
/// <remarks>
/// <para>
/// The feed is a file rather than a socket. <see cref="ReceiverPipeline.CreateHost"/> constructs its
/// own TCP stack and so cannot be pointed at a fake, but everything downstream of the receiver - the
/// part these tests exist to cover - is reached by building the <see cref="ReceiverHost"/> directly and
/// handing its raw-sentence stream to <see cref="ReceiverPipeline.CreateStorage"/>, which is the real
/// production wiring including retry, dead-lettering and replay.
/// </para>
/// <para>
/// A single <see cref="FakeTimeProvider"/> is shared by the host and the storage stack, so tag-block
/// timestamps, hourly blob paths and dead-letter replay sweeps all advance together and
/// deterministically.
/// </para>
/// </remarks>
[TestClass]
public class ReceiverEndToEndTests : AzuriteIntegrationTestBase
{
    private const string FeedPath = "/feed/ais.nm4";

    private static readonly DateTimeOffset Start = new(2026, 7, 22, 17, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Real sentences: a type 1 position report (MMSI 265547250) and both parts of a type 5 static
    /// report, so decoding has something multi-part to reassemble.
    /// </summary>
    private static readonly string[] FeedLines =
    [
        "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24",
        "!AIVDM,2,1,,A,55P5TL01VIaAL@7WKO4806<D18E8222222222216C8888888888888888800,0*33",
        "!AIVDM,2,2,,A,00000000000,2*25",
    ];

    [TestMethod]
    public async Task Feed_DecodesAndPersistsTaggedSentencesToTheHourlyBlob()
    {
        FakeTimeProvider time = new(Start);

        // One line already carries a tag block, stamped at a different instant so the assertion below
        // proves it was passed through untouched rather than re-tagged with the host's clock.
        string preTagged = "!AIVDM,1,1,,B,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"
            .PrependNmeaBlockTags(new FakeTimeProvider(Start.AddHours(-5)));

        string[] lines = [.. FeedLines, preTagged];

        List<Exception> persistErrors = [];
        List<long> drops = [];
        List<IAisMessage> messages = [];

        StorageConfig config = this.CreateStorageConfig();

        await using (ReceiverHost host = CreateHost(lines, time))
        {
            // Subscribe before starting: the host's subjects only publish when observed, and message
            // decoding is skipped entirely when nothing observes Messages - so this subscription is
            // what makes this a decode test rather than only a byte-transport test.
            using IDisposable messageSubscription = host.Messages.Subscribe(messages.Add);

            StorageBatchPipeline pipeline = CreateStorage(config, host, time, persistErrors, drops);

            await using (pipeline)
            {
                await host.StartAsync();
                await FlushAsync(pipeline);
            }
        }

        // Decoding really ran.
        messages.ShouldNotBeEmpty();
        messages.Select(m => m.Mmsi).ShouldContain(265547250u);

        // The blob holds every sentence, newline separated, with tag blocks prepended to the untagged
        // ones using the injected clock and the already-tagged one left alone.
        string expected = string.Concat(
            lines.Select(line => (line.IsMissingNmeaBlockTags() ? line.PrependNmeaBlockTags(time) : line) + "\n"));

        string path = AzureAppendBlobStorageClient.GetHourlyBlobPath(Start);
        (await this.DownloadTextAsync(path)).ShouldBe(expected);

        // Nothing leaked to a second blob, and neither failure path was taken.
        (await this.ListBlobPathsAsync()).ShouldHaveSingleItem().ShouldBe(path);
        persistErrors.ShouldBeEmpty();
        drops.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task WhenAppendFails_BatchIsDeadLetteredThenReplayedAfterRecovery()
    {
        FakeTimeProvider time = new(Start);
        string blockedPath = AzureAppendBlobStorageClient.GetHourlyBlobPath(Start);

        // Occupy the hour-17 path with a block blob. Appending to it is a blob-type conflict that
        // Azurite rejects in one round trip and the SDK does not retry, which makes the failure both
        // realistic (a real service error, over real HTTP) and instant. Pointing at an unreachable
        // endpoint instead would cost seconds of SDK backoff per attempt.
        await this.PreCreateBlockBlobAsync(blockedPath, "PRE-EXISTING\n");

        using TempDirectory deadLetters = new();
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        using MeterCollector collector = MeterCollector.ForMeters(meterFactory);

        List<Exception> persistErrors = [];
        List<long> drops = [];

        StorageConfig config = this.CreateStorageConfig(c =>
        {
            c.WriteBatchSize = FeedLines.Length; // the feed forms exactly one batch
            c.DeadLetterPath = deadLetters.Path;
            c.DeadLetterReplayIntervalSeconds = 60; // driven by the fake clock, not wall time
        });

        await using ReceiverHost host = CreateHost(FeedLines, time, metrics);
        StorageBatchPipeline pipeline = CreateStorage(config, host, time, persistErrors, drops, metrics);

        await using (pipeline)
        {
            await host.StartAsync();
            await FlushAsync(pipeline);

            // The batch was dead-lettered, not lost - and deliberately not surfaced as a persist
            // error: with a dead-letter path configured, ResilientStorageClient absorbs the failure
            // once the batch is safely on disk. That contract is what keeps a storage outage from
            // propagating into the receive loop, so it is worth pinning.
            Directory.GetFiles(deadLetters.Path, "*.nm4").ShouldHaveSingleItem();
            persistErrors.ShouldBeEmpty();
            drops.ShouldBeEmpty();

            // The pre-existing blob was not corrupted by the failed append.
            (await this.DownloadTextAsync(blockedPath)).ShouldBe("PRE-EXISTING\n");

            // Recovery: the hour rolls over, so the blocked path is no longer the target, and the same
            // clock tick fires the replayer's timer.
            time.Advance(TimeSpan.FromHours(1));
            string recoveredPath = AzureAppendBlobStorageClient.GetHourlyBlobPath(time.GetUtcNow());
            recoveredPath.ShouldNotBe(blockedPath);

            // The sweep is fire-and-forget from a timer callback, so this is the one place a bounded
            // poll is unavoidable. It normally settles in a few tens of milliseconds.
            await WaitUntilAsync(
                () => Task.FromResult(Directory.GetFiles(deadLetters.Path, "*.nm4").Length == 0),
                "the dead-letter directory to drain after the storage backend recovered");

            string expected = string.Concat(FeedLines.Select(line => line.PrependNmeaBlockTags(time) + "\n"));

            // The replayed batch reached storage intact and in feed order. Note the tag blocks were
            // written when the sentences were first received, so they carry the pre-rollover
            // timestamp - the dead-letter round trip preserves bytes rather than re-tagging.
            await WaitUntilAsync(
                async () => (await this.ListBlobPathsAsync()).Contains(recoveredPath),
                "the replayed batch to appear in storage");

            string replayed = await this.DownloadTextAsync(recoveredPath);
            replayed.ShouldBe(string.Concat(FeedLines.Select(line => line.PrependNmeaBlockTags(new FakeTimeProvider(Start)) + "\n")));
            replayed.ShouldNotBe(expected); // the replay did not re-stamp with the post-rollover clock
        }

        collector.Total("ais.storage.batches.failed").ShouldBe(1);
        collector.Total("ais.storage.batches.replayed").ShouldBe(1);
    }

    [TestMethod]
    public async Task WhenStorageStalls_ShedsLoadAndPersistsTheAcceptedSentences()
    {
        FakeTimeProvider time = new(Start);
        StorageConfig config = this.CreateStorageConfig();

        // A burst far larger than the buffers can hold.
        string[] burst = [.. Enumerable.Range(0, 200).Select(i => $"!AIVDM,1,1,,A,BURST{i:D3},0*00")];

        List<long> drops = [];
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The real blob client, behind a gate, so storage is genuinely stalled while the burst
        // arrives. Racing an unthrottled local Azurite would not reliably saturate the buffers.
        AzureAppendBlobStorageClient blobClient = new(config, time);
        GatedStorageClient storage = new(blobClient, gate.Task);

        // Minimal buffers at both stages: a 200-sentence burst cannot fit, so it must be shed.
        StorageBatchOptions options = new(
            WriteBatchSize: 1,
            BoundedCapacity: 1,
            BatchTimeout: TimeSpan.FromHours(1),
            MaxDegreeOfParallelism: 1,
            MaxPendingBatches: 1);

        await using ReceiverHost host = CreateHost(burst, time);

        StorageBatchPipeline pipeline = new(
            host.RawSentences,
            storage,
            options,
            metrics: null,
            onPersistError: static _ => { },
            onSentencesDropped: drops.Add);

        await using (pipeline)
        {
            // The host publishes raw sentences synchronously on its receive loop, so by the time
            // StartAsync returns every declined post - and its drop callback - has already run.
            await host.StartAsync();

            drops.ShouldNotBeEmpty();

            // The callback is throttled to the first drop and then every ten-thousandth, so the only
            // stable assertion is that it reported the first one. Asserting a count would be asserting
            // the throttle, and how much gets shed depends on timing.
            drops.ShouldContain(1);

            // Let the stalled writes through so the pipeline drains rather than hitting its teardown
            // timeout.
            gate.SetResult();
            await FlushAsync(pipeline);
        }

        // What did get through must be intact: whole sentences, in feed order, and fewer than were
        // offered. Shedding load is acceptable; corrupting or reordering what survives is not.
        string path = AzureAppendBlobStorageClient.GetHourlyBlobPath(Start);
        string[] persisted = (await this.DownloadTextAsync(path))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        persisted.ShouldNotBeEmpty();
        persisted.Length.ShouldBeLessThan(burst.Length);

        string[] expectedOrder = [.. burst.Select(line => line.PrependNmeaBlockTags(time))];
        persisted.ShouldBeSubsetOf(expectedOrder);
        persisted.ShouldBe([.. expectedOrder.Where(persisted.Contains)]);
    }

    [TestMethod]
    public async Task Feed_AcrossHourBoundary_WritesOneBlobPerHour()
    {
        FakeTimeProvider time = new(Start);
        StorageConfig config = this.CreateStorageConfig();

        string[] beforeRollover = ["!AIVDM,1,1,,A,HOUR17A,0*00", "!AIVDM,1,1,,A,HOUR17B,0*00"];
        string[] afterRollover = ["!AIVDM,1,1,,A,HOUR18A,0*00"];

        List<Exception> persistErrors = [];
        List<long> drops = [];

        // The first feed is sized to hit WriteBatchSize exactly, so it persists on its own. That
        // matters because FlushAsync is one-shot - it stops feeding and completes the batch block - so
        // it cannot be used to separate two feeds within one pipeline. Reaching the batch size is the
        // only way to get an intermediate write out of a pipeline that must survive to see the second
        // feed, which is precisely what a deployed receiver does across an hour boundary.
        config.WriteBatchSize = beforeRollover.Length;

        // One pipeline spanning two feeds, so a single long-lived storage client crosses the boundary.
        // The host's stream is forwarded into a subject rather than subscribed directly, because the
        // first feed completing would otherwise complete the pipeline's source too.
        Subject<ReadOnlyMemory<byte>> sentences = new();

        StorageBatchPipeline pipeline = ReceiverPipeline.CreateStorage(
            config,
            sentences,
            time,
            metrics: null,
            instrumentation: null,
            storageLogger: null,
            onPersistError: persistErrors.Add,
            onSentencesDropped: drops.Add)!;

        string hour17Path = AzureAppendBlobStorageClient.GetHourlyBlobPath(Start);
        string expectedHour17 = string.Concat(beforeRollover.Select(l => l.PrependNmeaBlockTags(time) + "\n"));

        await using (pipeline)
        {
            await using (ReceiverHost host = CreateHost(beforeRollover, time))
            {
                using IDisposable forward = host.RawSentences.Subscribe(sentences.OnNext);
                await host.StartAsync();
            }

            // Persistence of a size-triggered batch happens on the dataflow block's own thread, so
            // wait for it to land before moving the clock. Advancing first would let the batch be
            // written against the post-rollover hour and the test would prove nothing.
            await WaitUntilAsync(
                async () => (await this.ListBlobPathsAsync()).Contains(hour17Path),
                "the pre-rollover batch to be persisted to the hour-17 blob");

            time.Advance(TimeSpan.FromMinutes(35)); // 17:30 -> 18:05

            string expectedHour18 = string.Concat(afterRollover.Select(l => l.PrependNmeaBlockTags(time) + "\n"));
            string hour18Path = AzureAppendBlobStorageClient.GetHourlyBlobPath(time.GetUtcNow());
            hour18Path.ShouldNotBe(hour17Path);

            await using (ReceiverHost host = CreateHost(afterRollover, time))
            {
                using IDisposable forward = host.RawSentences.Subscribe(sentences.OnNext);
                await host.StartAsync();
            }

            // The second feed is a partial batch, so this single flush is what drives it out.
            await FlushAsync(pipeline);

            (await this.DownloadTextAsync(hour17Path)).ShouldBe(expectedHour17);
            (await this.DownloadTextAsync(hour18Path)).ShouldBe(expectedHour18);
            (await this.ListBlobPathsAsync()).OrderBy(p => p, StringComparer.Ordinal)
                .ShouldBe([hour17Path, hour18Path]);
        }

        persistErrors.ShouldBeEmpty();
        drops.ShouldBeEmpty();
    }

    private static ReceiverHost CreateHost(
        IEnumerable<string> lines,
        TimeProvider time,
        ApplicationMetrics? metrics = null)
    {
        FakeEnvironment environment = FakeEnvironment.CreateLinuxEnvironment();
        FakeFileSystem fileSystem = new(environment);
        fileSystem.CreateFile(new FilePath(FeedPath)).SetTextContent(string.Join('\n', lines));

        // No delay overload: that delay is real wall-clock time, one interval per line.
        FileStreamNmeaReceiver receiver = new(fileSystem, new FilePath(FeedPath));

        // retryAttempts 1 disables the host's Polly retry, so a defect surfaces as a failure rather
        // than as a multi-second stall repeated a hundred times.
        return new ReceiverHost(
            receiver,
            time,
            instrumentation: null,
            retryPeriodicity: TimeSpan.Zero,
            retryAttempts: 1,
            metrics: metrics);
    }

    private static StorageBatchPipeline CreateStorage(
        StorageConfig config,
        ReceiverHost host,
        TimeProvider time,
        List<Exception> persistErrors,
        List<long> drops,
        ApplicationMetrics? metrics = null) =>
        ReceiverPipeline.CreateStorage(
            config,
            host.RawSentences,
            time,
            metrics,
            instrumentation: null,
            storageLogger: null,
            onPersistError: persistErrors.Add,
            onSentencesDropped: drops.Add)!;

    /// <summary>
    /// Flushes the pipeline, failing the test if it does not drain. Awaiting the flush is what makes
    /// these tests deterministic: the batch pipeline's periodic trigger is a raw
    /// <see cref="System.Threading.Timer"/> that a fake clock cannot drive, so nothing may depend on
    /// it firing.
    /// </summary>
    private static async Task FlushAsync(StorageBatchPipeline pipeline)
    {
        bool completed = false;
        List<Exception> errors = [];

        await pipeline.FlushAsync(
            TimeSpan.FromSeconds(30),
            onCompleted: () => completed = true,
            onTimedOut: static () => Assert.Fail("The storage pipeline did not drain within 30 seconds."),
            onError: errors.Add);

        errors.ShouldBeEmpty();
        completed.ShouldBeTrue();
    }

    /// <summary>
    /// Delegates to a real storage client, but only once a gate is released, so a test can hold the
    /// storage stage stalled while it applies load.
    /// </summary>
    private sealed class GatedStorageClient(IStorageClient inner, Task gate) : IStorageClient
    {
        public async Task PersistAsync(IEnumerable<ReadOnlyMemory<byte>> messages)
        {
            await gate;
            await inner.PersistAsync(messages);
        }

        public void Dispose() => inner.Dispose();
    }
}
