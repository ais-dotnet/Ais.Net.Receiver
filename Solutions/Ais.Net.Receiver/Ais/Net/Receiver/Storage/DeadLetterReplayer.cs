// <copyright file="DeadLetterReplayer.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Storage;

/// <summary>
/// Periodically replays locally dead-lettered batches back to storage once the backend recovers, then
/// deletes each file that was successfully persisted. A sweep stops at the first batch that still
/// fails — that means storage is still unavailable, so the remaining files are left for the next sweep
/// (preserving order and avoiding a tight retry loop). Replaying and live writes reach the same
/// <see cref="IStorageClient"/>, which serializes them, so append order is never corrupted.
/// </summary>
public sealed class DeadLetterReplayer : IAsyncDisposable
{
    private readonly DeadLetterStore store;
    private readonly IStorageClient target;
    private readonly ILogger? logger;
    private readonly ApplicationMetrics? metrics;
    private readonly ITimer timer;
    private readonly CancellationTokenSource cts = new();
    private readonly SemaphoreSlim sweepGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadLetterReplayer"/> class and starts the sweep
    /// timer.
    /// </summary>
    /// <param name="store">The dead-letter store to drain.</param>
    /// <param name="target">The storage client to replay batches to (the underlying client, so a failed replay surfaces rather than being re-dead-lettered).</param>
    /// <param name="timeProvider">The time provider used to schedule sweeps.</param>
    /// <param name="interval">How often to sweep for pending dead-letter files.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional application metrics.</param>
    public DeadLetterReplayer(
        DeadLetterStore store,
        IStorageClient target,
        TimeProvider timeProvider,
        TimeSpan interval,
        ILogger? logger = null,
        ApplicationMetrics? metrics = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.logger = logger;
        this.metrics = metrics;

        // First sweep after one interval, then every interval.
        this.timer = timeProvider.CreateTimer(
            static state => _ = ((DeadLetterReplayer)state!).SweepSafeAsync(),
            this,
            interval,
            interval);
    }

    /// <summary>
    /// Replays each pending dead-letter batch to storage, deleting it on success. Stops at the first
    /// batch that still fails so it is retried on the next sweep. Exposed for testing; production sweeps
    /// are driven by the timer.
    /// </summary>
    /// <param name="cancellationToken">A token used to abandon the sweep during shutdown.</param>
    /// <returns>The number of batches successfully replayed.</returns>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pending = this.store.GetPendingFiles();
        if (pending.Count == 0)
        {
            return 0;
        }

        this.logger?.DeadLetterReplayStarting(pending.Count);

        int replayed = 0;
        foreach (string file in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ReadOnlyMemory<byte>> batch;
            try
            {
                batch = await DeadLetterStore.ReadBatchAsync(file).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A corrupt or locked file must not wedge the whole sweep; skip it and move on.
                this.logger?.DeadLetterReadFailed(file, ex);
                continue;
            }

            if (batch.Count == 0)
            {
                DeadLetterStore.Remove(file);
                continue;
            }

            try
            {
                await this.target.PersistAsync(batch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Storage is still unavailable: stop now and retry the remaining files next sweep.
                this.logger?.DeadLetterReplayDeferred(pending.Count - replayed, ex);
                break;
            }

            DeadLetterStore.Remove(file);
            replayed++;
            this.metrics?.StorageBatchesReplayed.Add(1);
            this.logger?.DeadLetterReplayed(file, batch.Count);
        }

        return replayed;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.cts.CancelAsync().ConfigureAwait(false);
        await this.timer.DisposeAsync().ConfigureAwait(false);

        // Wait for any in-flight sweep to observe cancellation and finish, so the caller can safely
        // dispose the target storage client afterwards. If a sweep is wedged in a slow write, bail out
        // rather than race its disposal.
        if (await this.sweepGate.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
        {
            this.sweepGate.Release();
            this.sweepGate.Dispose();
        }
        else
        {
            // A sweep is still wedged, so the gate stays alive for its Release; but the source is
            // already cancelled and the timer disposed, so nothing will register on the token again
            // (registration on an already-cancelled token runs inline) and it can be released here.
            this.logger?.DeadLetterReplayStopTimedOut();
        }

        this.cts.Dispose();
    }

    private async Task SweepSafeAsync()
    {
        // Non-reentrant: if the previous sweep is still running (e.g. a large backlog), skip this tick.
        if (!await this.sweepGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await this.SweepAsync(this.cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            this.logger?.DeadLetterReplaySweepFailed(ex);
        }
        finally
        {
            this.sweepGate.Release();
        }
    }
}
