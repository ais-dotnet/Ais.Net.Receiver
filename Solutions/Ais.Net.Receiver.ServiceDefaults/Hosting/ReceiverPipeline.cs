// <copyright file="ReceiverPipeline.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Receiver;
using Ais.Net.Receiver.Storage;
using Ais.Net.Receiver.Storage.Azure.Blob;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Hosting;

/// <summary>
/// Assembles the shared receive-and-persist wiring used by both the console and worker hosts, so the
/// receiver construction, the storage-client stack, and the batching pipeline live in one place
/// rather than being duplicated (and drifting) across hosts. The hosts differ only in how they
/// render output, which they supply through the pipeline's callbacks and their own subscriptions.
/// </summary>
public static class ReceiverPipeline
{
    private static readonly TimeSpan StorageRetryPeriodicity = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Builds a <see cref="ReceiverHost"/> and its underlying network receiver from the supplied AIS
    /// configuration.
    /// </summary>
    /// <param name="aisConfig">The AIS connection and receiver configuration.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="instrumentation">Optional application instrumentation for tracing.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <returns>A configured, not-yet-started <see cref="ReceiverHost"/>.</returns>
    public static ReceiverHost CreateHost(
        AisConfig aisConfig,
        TimeProvider timeProvider,
        ApplicationInstrumentation? instrumentation,
        ApplicationMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(aisConfig);

        INmeaReceiver receiver = new NetworkStreamNmeaReceiver(
            aisConfig.Connection.Host,
            aisConfig.Connection.Port,
            timeProvider,
            aisConfig.Connection.Retry.Periodicity,
            retryAttemptLimit: aisConfig.Connection.Retry.Attempts,
            metrics: metrics);

        return new ReceiverHost(
            receiver,
            timeProvider,
            instrumentation,
            retryPeriodicity: aisConfig.Receiver.Retry.Periodicity,
            retryAttempts: aisConfig.Receiver.Retry.Attempts,
            metrics: metrics);
    }

    /// <summary>
    /// Builds the storage batching pipeline that persists <see cref="ReceiverHost.RawSentences"/>
    /// through a resilient Azure append-blob client, or returns <see langword="null"/> when capture
    /// is disabled.
    /// </summary>
    /// <param name="storageConfig">The storage configuration.</param>
    /// <param name="rawSentences">The raw sentence stream to persist.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="instrumentation">Optional application instrumentation for tracing.</param>
    /// <param name="storageLogger">Optional logger for the storage client.</param>
    /// <param name="onPersistError">Invoked when a batch cannot be persisted.</param>
    /// <param name="onSentencesDropped">Invoked with the running dropped-sentence total when the buffer sheds load.</param>
    /// <returns>The pipeline, or <see langword="null"/> when <see cref="StorageConfig.EnableCapture"/> is <see langword="false"/>.</returns>
    public static StorageBatchPipeline? CreateStorage(
        StorageConfig storageConfig,
        IObservable<ReadOnlyMemory<byte>> rawSentences,
        TimeProvider timeProvider,
        ApplicationMetrics? metrics,
        ApplicationInstrumentation? instrumentation,
        ILogger? storageLogger,
        Action<Exception> onPersistError,
        Action<long> onSentencesDropped)
    {
        ArgumentNullException.ThrowIfNull(storageConfig);

        if (!storageConfig.EnableCapture)
        {
            return null;
        }

        IStorageClient blobStorageClient = new AzureAppendBlobStorageClient(
            storageConfig,
            timeProvider,
            metrics,
            instrumentation,
            storageLogger);

        IStorageClient resilientStorageClient = new ResilientStorageClient(
            blobStorageClient,
            timeProvider,
            storageConfig.WriteRetryAttempts,
            StorageRetryPeriodicity,
            storageConfig.DeadLetterPath,
            metrics,
            storageLogger);

        StorageBatchOptions options = new(
            storageConfig.WriteBatchSize,
            storageConfig.BoundedCapacity,
            TimeSpan.FromSeconds(storageConfig.BatchTimeoutSeconds),
            storageConfig.MaxDegreeOfParallelism);

        return new StorageBatchPipeline(
            rawSentences,
            resilientStorageClient,
            options,
            metrics,
            onPersistError,
            onSentencesDropped);
    }

    /// <summary>
    /// Classifies a decoding exception into a low-cardinality value for the <c>error.type</c> metric
    /// dimension.
    /// </summary>
    /// <param name="exception">The exception raised while decoding a sentence.</param>
    /// <returns>The metric dimension value.</returns>
    public static string ClassifyError(Exception exception) => exception switch
    {
        NotImplementedException => "unsupported_message",
        _ => "parse_error",
    };
}
