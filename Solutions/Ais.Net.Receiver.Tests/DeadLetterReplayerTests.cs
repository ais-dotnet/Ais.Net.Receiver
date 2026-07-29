using Ais.Net.Receiver.Storage;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class DeadLetterReplayerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    [TestMethod]
    public async Task SweepAsync_ReplaysPendingBatchesToStorageAndDeletesThem()
    {
        using TempDirectory dir = new();
        FakeTimeProvider time = new();
        DeadLetterStore store = new(dir.Path, time);

        await store.WriteAsync(["A1"u8.ToArray(), "A2"u8.ToArray()]);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await store.WriteAsync(["B1"u8.ToArray()]);

        List<int> replayedBatchSizes = [];
        IStorageClient target = Substitute.For<IStorageClient>();
        target.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>())
            .Returns(ci =>
            {
                replayedBatchSizes.Add(ci.Arg<IEnumerable<ReadOnlyMemory<byte>>>()!.Count());
                return Task.CompletedTask;
            });

        await using DeadLetterReplayer replayer = new(store, target, time, Interval);

        int replayed = await replayer.SweepAsync(CancellationToken.None);

        replayed.ShouldBe(2);
        store.GetPendingFiles().ShouldBeEmpty();

        // Oldest first: the two-sentence batch before the one-sentence batch.
        replayedBatchSizes.ShouldBe([2, 1]);
    }

    [TestMethod]
    public async Task SweepAsync_WhenStorageStillFailing_LeavesFilesAndStopsAtFirstFailure()
    {
        using TempDirectory dir = new();
        FakeTimeProvider time = new();
        DeadLetterStore store = new(dir.Path, time);

        await store.WriteAsync(["A"u8.ToArray()]);
        await store.WriteAsync(["B"u8.ToArray()]);

        IStorageClient target = Substitute.For<IStorageClient>();
        target.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>())
            .Returns(_ => Task.FromException(new IOException("storage down")));

        await using DeadLetterReplayer replayer = new(store, target, time, Interval);

        int replayed = await replayer.SweepAsync(CancellationToken.None);

        replayed.ShouldBe(0);

        // Nothing lost - both files remain for the next sweep...
        store.GetPendingFiles().Count.ShouldBe(2);

        // ...and the sweep stopped after the first failure rather than hammering a down backend.
        await target.Received(1).PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>());
    }

    [TestMethod]
    public async Task SweepAsync_AfterStorageRecovers_ReplaysTheBatchThatWasLeftBehind()
    {
        using TempDirectory dir = new();
        FakeTimeProvider time = new();
        DeadLetterStore store = new(dir.Path, time);

        await store.WriteAsync(["A"u8.ToArray()]);

        bool storageUp = false;
        IStorageClient target = Substitute.For<IStorageClient>();
        target.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>())
            .Returns(_ => storageUp ? Task.CompletedTask : Task.FromException(new IOException("storage down")));

        await using DeadLetterReplayer replayer = new(store, target, time, Interval);

        // First sweep while storage is down: batch is preserved.
        (await replayer.SweepAsync(CancellationToken.None)).ShouldBe(0);
        store.GetPendingFiles().Count.ShouldBe(1);

        // Connection restored: the next sweep drains it.
        storageUp = true;
        (await replayer.SweepAsync(CancellationToken.None)).ShouldBe(1);
        store.GetPendingFiles().ShouldBeEmpty();
    }
}
