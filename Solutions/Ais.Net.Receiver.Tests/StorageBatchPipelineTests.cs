using System.Reactive.Subjects;

using Ais.Net.Receiver.Hosting;
using Ais.Net.Receiver.Storage;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class StorageBatchPipelineTests
{
    [TestMethod]
    public async Task FlushAsync_PersistsBufferedSentencesAndReportsCompletion()
    {
        Subject<ReadOnlyMemory<byte>> source = new();
        RecordingStorageClient storage = new();
        List<Exception> persistErrors = [];
        bool completed = false;

        // Batch size far above what we send and an effectively infinite timer, so the only thing
        // that can flush the partial batch is FlushAsync itself.
        StorageBatchOptions options = new(WriteBatchSize: 100, BoundedCapacity: 1000, BatchTimeout: TimeSpan.FromHours(1), MaxDegreeOfParallelism: 1);

        await using StorageBatchPipeline pipeline = new(
            source, storage, options, metrics: null,
            onPersistError: persistErrors.Add,
            onSentencesDropped: static _ => { });

        source.OnNext("!AIVDM,1,1,,A,aaaa,0*00"u8.ToArray());
        source.OnNext("!AIVDM,1,1,,A,bbbb,0*00"u8.ToArray());

        await pipeline.FlushAsync(
            TimeSpan.FromSeconds(30),
            onCompleted: () => completed = true,
            onTimedOut: static () => { },
            onError: static _ => { });

        completed.ShouldBeTrue();
        persistErrors.ShouldBeEmpty();
        storage.Persisted.Count.ShouldBe(2);
    }

    [TestMethod]
    public async Task ReachingBatchSize_PersistsWithoutFlush()
    {
        Subject<ReadOnlyMemory<byte>> source = new();
        TaskCompletionSource persisted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingStorageClient storage = new(onPersist: () =>
        {
            persisted.TrySetResult();
            return Task.CompletedTask;
        });

        StorageBatchOptions options = new(WriteBatchSize: 3, BoundedCapacity: 100, BatchTimeout: TimeSpan.FromHours(1), MaxDegreeOfParallelism: 1);

        await using StorageBatchPipeline pipeline = new(
            source, storage, options, metrics: null,
            onPersistError: static _ => { },
            onSentencesDropped: static _ => { });

        source.OnNext("a"u8.ToArray());
        source.OnNext("b"u8.ToArray());
        source.OnNext("c"u8.ToArray()); // reaching WriteBatchSize triggers a batch on its own

        await persisted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        storage.Persisted.Count.ShouldBe(3);
    }

    [TestMethod]
    public async Task PersistFailure_IsReportedToPersistErrorCallback()
    {
        Subject<ReadOnlyMemory<byte>> source = new();
        RecordingStorageClient storage = new(onPersist: static () => throw new IOException("boom"));
        TaskCompletionSource errorSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Exception> errors = [];

        // Batch size 1 so a single sentence forms a batch and is persisted immediately.
        StorageBatchOptions options = new(WriteBatchSize: 1, BoundedCapacity: 100, BatchTimeout: TimeSpan.FromHours(1), MaxDegreeOfParallelism: 1);

        await using StorageBatchPipeline pipeline = new(
            source, storage, options, metrics: null,
            onPersistError: ex =>
            {
                errors.Add(ex);
                errorSeen.TrySetResult();
            },
            onSentencesDropped: static _ => { });

        source.OnNext("!AIVDM,1,1,,A,aaaa,0*00"u8.ToArray());

        await errorSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        errors.ShouldHaveSingleItem().ShouldBeOfType<IOException>();
    }

    private sealed class RecordingStorageClient : IStorageClient
    {
        private readonly List<byte[]> persisted = [];
        private readonly Func<Task>? onPersist;

        public RecordingStorageClient(Func<Task>? onPersist = null) => this.onPersist = onPersist;

        public IReadOnlyList<byte[]> Persisted
        {
            get
            {
                lock (this.persisted)
                {
                    return [.. this.persisted];
                }
            }
        }

        public async Task PersistAsync(IEnumerable<ReadOnlyMemory<byte>> messages)
        {
            if (this.onPersist is not null)
            {
                await this.onPersist();
            }

            lock (this.persisted)
            {
                foreach (ReadOnlyMemory<byte> message in messages)
                {
                    this.persisted.Add(message.ToArray());
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
