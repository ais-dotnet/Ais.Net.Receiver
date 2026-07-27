// <copyright file="ApplicationMetrics.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics.Metrics;

namespace Ais.Net.Receiver.Telemetry;

/// <summary>
/// Centralized metrics for the Ais.Net.Receiver application.
/// Uses IMeterFactory for proper DI integration and testability.
/// Register as singleton in DI container.
/// </summary>
public sealed class ApplicationMetrics : IDisposable
{
    /// <summary>
    /// The meter name used for metric identification.
    /// </summary>
    public const string MeterName = "Ais.Net.Receiver";

    private readonly Meter meter;
    private long batchesPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">The meter factory for creating meters.</param>
    public ApplicationMetrics(IMeterFactory meterFactory)
    {
        this.meter = meterFactory.Create(new MeterOptions(MeterName)
        {
            Version = ApplicationInstrumentation.ServiceVersion,
        });

        // Counters for cumulative values
        this.MessagesReceived = this.meter.CreateCounter<long>(
            "ais.receiver.messages.received",
            unit: "{message}",
            description: "Number of AIS messages successfully received and parsed");

        this.SentencesReceived = this.meter.CreateCounter<long>(
            "ais.receiver.sentences.received",
            unit: "{sentence}",
            description: "Number of NMEA sentences received");

        this.SentencesDropped = this.meter.CreateCounter<long>(
            "ais.receiver.sentences.dropped",
            unit: "{sentence}",
            description: "Number of NMEA sentences dropped because the storage batch buffer was full");

        this.ErrorsReceived = this.meter.CreateCounter<long>(
            "ais.receiver.errors",
            unit: "{error}",
            description: "Number of parsing or processing errors");

        this.StorageWriteOperations = this.meter.CreateCounter<long>(
            "ais.storage.write.operations",
            unit: "{operation}",
            description: "Number of storage write operations");

        this.StorageBytesWritten = this.meter.CreateCounter<long>(
            "ais.storage.bytes.written",
            unit: "By",
            description: "Total bytes written to storage");

        this.StorageBatchesFailed = this.meter.CreateCounter<long>(
            "ais.storage.batches.failed",
            unit: "{batch}",
            description: "Number of batches abandoned or dead-lettered after storage write retries were exhausted");

        this.StorageBatchesReplayed = this.meter.CreateCounter<long>(
            "ais.storage.batches.replayed",
            unit: "{batch}",
            description: "Number of dead-lettered batches successfully replayed to storage after the backend recovered");

        this.ConnectionAttempts = this.meter.CreateCounter<long>(
            "ais.receiver.connection.attempts",
            unit: "{attempt}",
            description: "Number of connection attempts to AIS data source");

        this.ConnectionFailures = this.meter.CreateCounter<long>(
            "ais.receiver.connection.failures",
            unit: "{failure}",
            description: "Number of failed connection attempts");

        this.RetryAttempts = this.meter.CreateCounter<long>(
            "ais.receiver.retry.attempts",
            unit: "{attempt}",
            description: "Number of reconnection attempts, tagged by component");

        // An up-down counter rather than a counter: this is a level, not a total. It answers "are we
        // failing right now", which a monotonic failure count cannot, and resets to zero on connect.
        this.ConsecutiveConnectionFailures = this.meter.CreateUpDownCounter<long>(
            "ais.receiver.connection.consecutive_failures",
            unit: "{failure}",
            description: "Consecutive connection failures; resets to zero on a successful connection");

        // Histograms for distributions. Explicit bucket boundaries matter here: the OpenTelemetry
        // defaults (0, 5, 10, 25, ... 10000) are a poor fit for both ends of this workload - message
        // processing is sub-millisecond, and blob writes run to tens of seconds - so the defaults
        // collapse most observations into the first or last bucket and lose all percentile detail.

        // Message processing: sub-millisecond to ~100ms.
        this.MessageProcessingDuration = this.meter.CreateHistogram<double>(
            "ais.receiver.message.processing.duration",
            unit: "ms",
            description: "Duration of message processing in milliseconds",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [0.1, 0.5, 1, 2.5, 5, 10, 25, 50, 100],
            });

        // Storage writes: ~10ms to 30s for blob operations.
        this.StorageWriteDuration = this.meter.CreateHistogram<double>(
            "ais.storage.write.duration",
            unit: "ms",
            description: "Duration of storage write operations in milliseconds",
            advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000],
            });

        // Batch sizes: 1 to 10000 messages.
        this.BatchSize = this.meter.CreateHistogram<long>(
            "ais.receiver.batch.size",
            unit: "{message}",
            description: "Number of messages per batch write operation",
            advice: new InstrumentAdvice<long>
            {
                HistogramBucketBoundaries = [1, 10, 50, 100, 250, 500, 1000, 2500, 5000, 10000],
            });

        // Observable gauge for storage backlog
        this.meter.CreateObservableGauge(
            "ais.storage.batches.pending",
            () => Volatile.Read(ref this.batchesPending),
            unit: "{batch}",
            description: "Number of batches pending storage write");
    }

    /// <summary>
    /// Gets the counter for received AIS messages.
    /// </summary>
    public Counter<long> MessagesReceived { get; }

    /// <summary>
    /// Gets the counter for received NMEA sentences.
    /// </summary>
    public Counter<long> SentencesReceived { get; }

    /// <summary>
    /// Gets the counter for NMEA sentences dropped because the storage batch buffer was full.
    /// </summary>
    public Counter<long> SentencesDropped { get; }

    /// <summary>
    /// Gets the counter for errors encountered during processing.
    /// </summary>
    public Counter<long> ErrorsReceived { get; }

    /// <summary>
    /// Gets the counter for storage write operations.
    /// </summary>
    public Counter<long> StorageWriteOperations { get; }

    /// <summary>
    /// Gets the counter for bytes written to storage.
    /// </summary>
    public Counter<long> StorageBytesWritten { get; }

    /// <summary>
    /// Gets the counter for batches abandoned or dead-lettered after storage write retries were exhausted.
    /// </summary>
    public Counter<long> StorageBatchesFailed { get; }

    /// <summary>
    /// Gets the counter for dead-lettered batches successfully replayed to storage after the backend recovered.
    /// </summary>
    public Counter<long> StorageBatchesReplayed { get; }

    /// <summary>
    /// Gets the counter for connection attempts.
    /// </summary>
    public Counter<long> ConnectionAttempts { get; }

    /// <summary>
    /// Gets the counter for failed connection attempts.
    /// </summary>
    public Counter<long> ConnectionFailures { get; }

    /// <summary>
    /// Gets the counter for reconnection attempts.
    /// </summary>
    public Counter<long> RetryAttempts { get; }

    /// <summary>
    /// Gets the up-down counter tracking consecutive connection failures, which resets to zero
    /// whenever a connection succeeds.
    /// </summary>
    public UpDownCounter<long> ConsecutiveConnectionFailures { get; }

    /// <summary>
    /// Gets the histogram for message processing duration.
    /// </summary>
    public Histogram<double> MessageProcessingDuration { get; }

    /// <summary>
    /// Gets the histogram for storage write duration.
    /// </summary>
    public Histogram<double> StorageWriteDuration { get; }

    /// <summary>
    /// Gets the histogram for batch sizes.
    /// </summary>
    public Histogram<long> BatchSize { get; }

    /// <summary>
    /// Sets the number of batches pending storage write.
    /// </summary>
    /// <param name="count">The number of pending batches.</param>
    public void SetBatchesPending(long count) => Volatile.Write(ref this.batchesPending, count);

    /// <inheritdoc/>
    public void Dispose()
    {
        this.meter.Dispose();
    }
}
