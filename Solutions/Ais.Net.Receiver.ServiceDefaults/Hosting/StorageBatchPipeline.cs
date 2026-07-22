// <copyright file="StorageBatchPipeline.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

using Ais.Net.Receiver.Receiver;
using Ais.Net.Receiver.Storage;
using Ais.Net.Receiver.Telemetry;

namespace Ais.Net.Receiver.Hosting;

/// <summary>
/// Batches raw NMEA sentence bytes from a stream and persists each batch through an
/// <see cref="IStorageClient"/>. Both stages are bounded, so if the storage backend stalls the buffers
/// fill, backpressure propagates back to the source, and load is shed through the drop callback rather
/// than memory growing without limit; a timer flushes partial batches so latency stays bounded even at
/// low message rates. Both the console and worker hosts share this pipeline so the batching,
/// backpressure, and shutdown-flush logic lives in one place; the hosts supply only the sink-specific
/// logging via the callbacks.
/// </summary>
public sealed class StorageBatchPipeline : IAsyncDisposable
{
    private readonly IStorageClient storageClient;
    private readonly ApplicationMetrics? metrics;
    private readonly BatchBlock<ReadOnlyMemory<byte>> batchBlock;
    private readonly ActionBlock<IEnumerable<ReadOnlyMemory<byte>>> actionBlock;
    private readonly Timer batchTimer;
    private readonly IDisposable subscription;
    private readonly Action<Exception> onPersistError;
    private readonly Action<long> onSentencesDropped;
    private readonly DeadLetterReplayer? replayer;
    private long droppedSentences;
    private int stopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageBatchPipeline"/> class and immediately
    /// begins consuming <paramref name="rawSentences"/>.
    /// </summary>
    /// <param name="rawSentences">The stream of raw NMEA sentence bytes to persist.</param>
    /// <param name="storageClient">The storage client that persists each batch. Owned by this pipeline and disposed with it.</param>
    /// <param name="options">The batching options (batch size, buffer capacity, flush interval, parallelism).</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="onPersistError">Invoked when a batch cannot be persisted (after the pipeline records the exception on the current activity).</param>
    /// <param name="onSentencesDropped">Invoked with the running dropped-sentence total when the bounded buffer sheds load (throttled: first drop, then every ten-thousandth).</param>
    /// <param name="replayer">Optional dead-letter replayer; when supplied it is owned by this pipeline and stopped on disposal before the storage client is disposed.</param>
    public StorageBatchPipeline(
        IObservable<ReadOnlyMemory<byte>> rawSentences,
        IStorageClient storageClient,
        StorageBatchOptions options,
        ApplicationMetrics? metrics,
        Action<Exception> onPersistError,
        Action<long> onSentencesDropped,
        DeadLetterReplayer? replayer = null)
    {
        ArgumentNullException.ThrowIfNull(rawSentences);
        ArgumentNullException.ThrowIfNull(storageClient);
        ArgumentNullException.ThrowIfNull(onPersistError);
        ArgumentNullException.ThrowIfNull(onSentencesDropped);

        this.storageClient = storageClient;
        this.metrics = metrics;
        this.onPersistError = onPersistError;
        this.onSentencesDropped = onSentencesDropped;
        this.replayer = replayer;

        this.batchBlock = new BatchBlock<ReadOnlyMemory<byte>>(
            options.WriteBatchSize,
            new GroupingDataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });

        this.actionBlock = new ActionBlock<IEnumerable<ReadOnlyMemory<byte>>>(
            this.PersistBatchAsync,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,

                // Bound the action block so a stalled storage backend cannot grow this queue without
                // limit. When it fills, the batch block backs up to its own bounded capacity and then
                // sheds load through the backpressure callback - so total buffered memory is bounded
                // end-to-end and drops are surfaced rather than the process eventually running out of
                // memory. The batch block remains the main (configurable) buffer; this is just enough
                // headroom to keep every persist worker fed plus one batch ready to hand off.
                BoundedCapacity = options.MaxPendingBatches,
            });

        this.batchBlock.LinkTo(this.actionBlock, new DataflowLinkOptions { PropagateCompletion = true });

        this.batchTimer = new Timer(
            static state => ((BatchBlock<ReadOnlyMemory<byte>>)state!).TriggerBatch(),
            this.batchBlock,
            options.BatchTimeout,
            options.BatchTimeout);

        // Feed sentences into the batch, surfacing backpressure drops instead of losing them
        // silently: a full bounded block declines Post, which SubscribeWithBackpressure reports.
        BatchBlock<ReadOnlyMemory<byte>> block = this.batchBlock;
        this.subscription = rawSentences.SubscribeWithBackpressure(block.Post, this.OnDropped);
    }

    /// <summary>
    /// Stops accepting new sentences, flushes the buffered batch, and waits up to
    /// <paramref name="timeout"/> for in-flight persistence to drain. The outcome is reported via
    /// the callbacks so each host can log it in its own sink.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for the pipeline to drain.</param>
    /// <param name="onCompleted">Invoked when the pipeline drained within the timeout.</param>
    /// <param name="onTimedOut">Invoked when the pipeline did not drain within the timeout.</param>
    /// <param name="onError">Invoked when draining faulted.</param>
    /// <returns>A task that completes once the flush has resolved (successfully or otherwise).</returns>
    public async Task FlushAsync(TimeSpan timeout, Action onCompleted, Action onTimedOut, Action<Exception> onError)
    {
        await this.StopFeedingAsync().ConfigureAwait(false);
        this.batchBlock.Complete();

        try
        {
            using CancellationTokenSource cts = new(timeout);
            await this.actionBlock.Completion.WaitAsync(cts.Token).ConfigureAwait(false);
            onCompleted();
        }
        catch (OperationCanceledException)
        {
            onTimedOut();
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.StopFeedingAsync().ConfigureAwait(false);

        // Stop replay before disposing the storage client it writes through.
        if (this.replayer is not null)
        {
            await this.replayer.DisposeAsync().ConfigureAwait(false);
        }

        this.storageClient.Dispose();
    }

    private async Task PersistBatchAsync(IEnumerable<ReadOnlyMemory<byte>> batch)
    {
        this.metrics?.SetBatchesPending(this.batchBlock.OutputCount);

        try
        {
            await this.storageClient.PersistAsync(batch).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Activity.Current?.RecordExceptionWithStatus(ex);
            this.onPersistError(ex);
        }
    }

    private void OnDropped()
    {
        this.metrics?.SentencesDropped.Add(1);
        long total = Interlocked.Increment(ref this.droppedSentences);
        if (total == 1 || total % 10_000 == 0)
        {
            this.onSentencesDropped(total);
        }
    }

    private async ValueTask StopFeedingAsync()
    {
        // Idempotent: FlushAsync and DisposeAsync may both run, in either order.
        if (Interlocked.Exchange(ref this.stopped, 1) == 0)
        {
            this.subscription.Dispose();
            await this.batchTimer.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The tuning knobs for a <see cref="StorageBatchPipeline"/>.
/// </summary>
/// <param name="WriteBatchSize">The number of sentences per persisted batch.</param>
/// <param name="BoundedCapacity">The maximum number of pending sentences buffered in the batch block before backpressure sheds load.</param>
/// <param name="BatchTimeout">The interval at which a partial batch is force-flushed.</param>
/// <param name="MaxDegreeOfParallelism">The maximum number of batches persisted concurrently.</param>
/// <param name="MaxPendingBatches">The maximum number of formed batches queued at the persist stage; bounds downstream memory so backpressure propagates back to the batch block. Should be at least <paramref name="MaxDegreeOfParallelism"/> so every persist worker can stay busy.</param>
public readonly record struct StorageBatchOptions(
    int WriteBatchSize,
    int BoundedCapacity,
    TimeSpan BatchTimeout,
    int MaxDegreeOfParallelism,
    int MaxPendingBatches);
