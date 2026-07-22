// <copyright file="ResilientStorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Resilience;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;

using Polly;

namespace Ais.Net.Receiver.Storage;

/// <summary>
/// Decorates an <see cref="IStorageClient"/> with bounded retries and optional dead-lettering, so a
/// transient storage outage does not silently drop a batch. Retries sit on top of the underlying
/// client's own transient-fault handling to survive longer outages; a batch that still fails is
/// either written to a local dead-letter directory (if configured) or surfaced to the caller.
/// </summary>
public sealed class ResilientStorageClient : IStorageClient
{
    private readonly IStorageClient inner;
    private readonly TimeProvider timeProvider;
    private readonly ResiliencePipeline retryPipeline;
    private readonly string? deadLetterPath;
    private readonly ApplicationMetrics? metrics;
    private readonly ILogger? logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilientStorageClient"/> class.
    /// </summary>
    /// <param name="inner">The storage client to decorate.</param>
    /// <param name="timeProvider">The time provider (used to name dead-letter files).</param>
    /// <param name="maxAttempts">Maximum number of attempts to persist a batch.</param>
    /// <param name="retryPeriodicity">Delay between attempts.</param>
    /// <param name="deadLetterPath">Optional local directory for batches that still fail; null to rethrow instead.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="logger">Optional logger.</param>
    public ResilientStorageClient(
        IStorageClient inner,
        TimeProvider timeProvider,
        int maxAttempts,
        TimeSpan retryPeriodicity,
        string? deadLetterPath = null,
        ApplicationMetrics? metrics = null,
        ILogger? logger = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.timeProvider = timeProvider;
        this.retryPipeline = RetryPipelines.ConstantDelay(retryPeriodicity, maxAttempts);
        this.deadLetterPath = deadLetterPath;
        this.metrics = metrics;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task PersistAsync(IEnumerable<ReadOnlyMemory<byte>> messages)
    {
        // Materialize once so every attempt re-persists the same batch.
        IReadOnlyList<ReadOnlyMemory<byte>> batch = messages as IReadOnlyList<ReadOnlyMemory<byte>> ?? messages.ToList();

        try
        {
            await this.retryPipeline.ExecuteAsync(
                async _ => await this.inner.PersistAsync(batch).ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            this.metrics?.StorageBatchesFailed.Add(1);

            if (this.deadLetterPath is not null)
            {
                await this.WriteDeadLetterAsync(batch).ConfigureAwait(false);
                return;
            }

            // No dead-letter configured - surface it so the caller logs/handles the loss.
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => this.inner.Dispose();

    private async Task WriteDeadLetterAsync(IReadOnlyList<ReadOnlyMemory<byte>> batch)
    {
        Directory.CreateDirectory(this.deadLetterPath!);
        string file = Path.Combine(
            this.deadLetterPath!,
            $"deadletter-{this.timeProvider.GetUtcNow():yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.nm4");

        await using (FileStream stream = new(file, FileMode.CreateNew, FileAccess.Write))
        {
            foreach (ReadOnlyMemory<byte> message in batch)
            {
                await stream.WriteAsync(message).ConfigureAwait(false);
                stream.WriteByte((byte)'\n');
            }
        }

        this.logger?.StorageBatchDeadLettered(batch.Count, file, new IOException("storage write retries exhausted"));
    }
}
