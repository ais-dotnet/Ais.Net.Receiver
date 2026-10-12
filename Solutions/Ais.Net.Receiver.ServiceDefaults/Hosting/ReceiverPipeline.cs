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
    /// configuration. The transport is selected by <see cref="AisConnectionConfig.Transport"/>; see
    /// <see cref="CreateReceiver"/>.
    /// </summary>
    /// <param name="aisConfig">The AIS connection and receiver configuration.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="instrumentation">Optional application instrumentation for tracing.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="onConnectionStateChanged">Optional callback invoked with the live connection state (true on connect, false on any disconnect/idle/error), e.g. to drive a health check.</param>
    /// <param name="loggerFactory">
    /// Optional factory used to create loggers for the receiver and its stream reader or connection. Without it
    /// both fall back to <c>NullLogger</c>, which silently discards the connection and socket-level
    /// diagnostics (event ids 4000-4024) that are the only way to tell a refused connection from a
    /// DNS failure from a feed that accepts and then goes quiet.
    /// </param>
    /// <returns>A configured, not-yet-started <see cref="ReceiverHost"/>.</returns>
    public static ReceiverHost CreateHost(
        AisConfig aisConfig,
        TimeProvider timeProvider,
        ApplicationInstrumentation? instrumentation,
        ApplicationMetrics? metrics,
        Action<bool>? onConnectionStateChanged = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(aisConfig);

        INmeaReceiver receiver = CreateReceiver(
            aisConfig.Connection,
            timeProvider,
            instrumentation,
            metrics,
            onConnectionStateChanged,
            loggerFactory);

        return new ReceiverHost(
            receiver,
            timeProvider,
            instrumentation,
            retryPeriodicity: aisConfig.Receiver.Retry.Periodicity,
            retryAttempts: aisConfig.Receiver.Retry.Attempts,
            metrics: metrics);
    }

    /// <summary>
    /// Builds the <see cref="INmeaReceiver"/> for the supplied connection configuration: an
    /// <see cref="NmeaReceiver"/> over the transport's <see cref="INmeaConnection"/>. The reconnect,
    /// idle-timeout, metrics and tracing behaviour lives in the receiver and is identical for every
    /// transport; only the connection underneath differs.
    /// </summary>
    /// <param name="connection">The connection configuration.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="instrumentation">Optional application instrumentation for tracing.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="onConnectionStateChanged">Optional callback invoked with the live connection state.</param>
    /// <param name="loggerFactory">Optional factory used to create loggers for the receiver and its connection.</param>
    /// <returns>The receiver.</returns>
    /// <exception cref="ArgumentException">The configuration names a transport this factory does not support, or the WebSocket URL is missing or invalid.</exception>
    public static INmeaReceiver CreateReceiver(
        AisConnectionConfig connection,
        TimeProvider timeProvider,
        ApplicationInstrumentation? instrumentation,
        ApplicationMetrics? metrics,
        Action<bool>? onConnectionStateChanged = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        INmeaConnection source = CreateConnection(connection, loggerFactory);

        NmeaReceiverOptions options = new()
        {
            RetryPeriodicity = connection.Retry.Periodicity,
            RetryAttemptLimit = connection.Retry.Attempts,
        };

        return new NmeaReceiver(
            source,
            timeProvider,
            options,
            metrics,
            loggerFactory?.CreateLogger<NmeaReceiver>(),
            onConnectionStateChanged,
            instrumentation);
    }

    /// <summary>
    /// Builds the <see cref="INmeaConnection"/> for the supplied connection configuration.
    /// </summary>
    /// <param name="connection">The connection configuration.</param>
    /// <param name="loggerFactory">Optional factory used to create the connection's logger.</param>
    /// <returns>The connection, not yet connected.</returns>
    /// <exception cref="ArgumentException">The configuration names a transport this factory does not support, or the WebSocket URL is missing or invalid.</exception>
    public static INmeaConnection CreateConnection(AisConnectionConfig connection, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        switch (connection.Transport)
        {
            case AisTransport.Tcp:
                return new TcpNmeaConnection(
                    connection.Host,
                    connection.Port,
                    loggerFactory?.CreateLogger<TcpNmeaConnection>());

            case AisTransport.WebSocket:
                // Options validation has already rejected a missing or non-ws(s) URL by the time a
                // host gets here; this is the last line of defence for direct callers.
                if (!Uri.TryCreate(connection.Url, UriKind.Absolute, out Uri? url))
                {
                    throw new ArgumentException($"'{nameof(AisConnectionConfig.Url)}' must be an absolute ws:// or wss:// URL when the transport is {nameof(AisTransport.WebSocket)}.", nameof(connection));
                }

                return new WebSocketNmeaConnection(
                    url,
                    loggerFactory?.CreateLogger<WebSocketNmeaConnection>());

            default:
                throw new ArgumentException($"Unsupported transport '{connection.Transport}'.", nameof(connection));
        }
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
    /// <param name="onSourceFaulted">Invoked when the sentence stream itself faults, after the pipeline has been completed so buffered batches still drain.</param>
    /// <returns>The pipeline, or <see langword="null"/> when <see cref="StorageConfig.EnableCapture"/> is <see langword="false"/>.</returns>
    public static StorageBatchPipeline? CreateStorage(
        StorageConfig storageConfig,
        IObservable<ReadOnlyMemory<byte>> rawSentences,
        TimeProvider timeProvider,
        ApplicationMetrics? metrics,
        ApplicationInstrumentation? instrumentation,
        ILogger? storageLogger,
        Action<Exception> onPersistError,
        Action<long> onSentencesDropped,
        Action<Exception>? onSourceFaulted = null)
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

        // When dead-lettering is enabled, run a background replayer that drains the local dead-letter
        // directory back to storage once it recovers. It writes through the underlying blob client
        // (which serializes appends), so replayed batches never race live capture.
        DeadLetterReplayer? replayer = null;
        if (storageConfig.DeadLetterPath is not null)
        {
            DeadLetterStore deadLetterStore = new(storageConfig.DeadLetterPath, timeProvider);
            replayer = new DeadLetterReplayer(
                deadLetterStore,
                blobStorageClient,
                timeProvider,
                TimeSpan.FromSeconds(storageConfig.DeadLetterReplayIntervalSeconds),
                storageLogger,
                metrics);
        }

        StorageBatchOptions options = new(
            storageConfig.WriteBatchSize,
            storageConfig.BoundedCapacity,
            TimeSpan.FromSeconds(storageConfig.BatchTimeoutSeconds),
            storageConfig.MaxDegreeOfParallelism,

            // One queued batch per persist worker plus one ready to hand off: enough to keep the
            // workers fed without letting the persist queue grow unbounded during a storage stall.
            MaxPendingBatches: storageConfig.MaxDegreeOfParallelism + 1);

        return new StorageBatchPipeline(
            rawSentences,
            resilientStorageClient,
            options,
            metrics,
            onPersistError,
            onSentencesDropped,
            replayer,
            onSourceFaulted);
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
