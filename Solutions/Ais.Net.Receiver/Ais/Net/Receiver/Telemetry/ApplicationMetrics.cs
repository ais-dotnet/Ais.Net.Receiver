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
        this.meter = meterFactory.Create(MeterName);

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

        this.ConnectionAttempts = this.meter.CreateCounter<long>(
            "ais.receiver.connection.attempts",
            unit: "{attempt}",
            description: "Number of connection attempts to AIS data source");

        this.ConnectionFailures = this.meter.CreateCounter<long>(
            "ais.receiver.connection.failures",
            unit: "{failure}",
            description: "Number of failed connection attempts");

        // Histograms for distributions
        this.MessageProcessingDuration = this.meter.CreateHistogram<double>(
            "ais.receiver.message.processing.duration",
            unit: "ms",
            description: "Duration of message processing in milliseconds");

        this.StorageWriteDuration = this.meter.CreateHistogram<double>(
            "ais.storage.write.duration",
            unit: "ms",
            description: "Duration of storage write operations in milliseconds");

        this.BatchSize = this.meter.CreateHistogram<long>(
            "ais.receiver.batch.size",
            unit: "{message}",
            description: "Number of messages per batch write operation");

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
    /// Gets the counter for connection attempts.
    /// </summary>
    public Counter<long> ConnectionAttempts { get; }

    /// <summary>
    /// Gets the counter for failed connection attempts.
    /// </summary>
    public Counter<long> ConnectionFailures { get; }

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
