// <copyright file="ApplicationMetricsTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics.Metrics;

using Ais.Net.Receiver.Telemetry;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ApplicationMetricsTests
{
    [TestMethod]
    public void Constructor_CreatesAllCounters()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();

        // Act
        ApplicationMetrics metrics = new(meterFactory);

        // Assert
        metrics.MessagesReceived.ShouldNotBeNull();
        metrics.SentencesReceived.ShouldNotBeNull();
        metrics.ErrorsReceived.ShouldNotBeNull();
        metrics.StorageWriteOperations.ShouldNotBeNull();
        metrics.StorageBytesWritten.ShouldNotBeNull();
        metrics.ConnectionAttempts.ShouldNotBeNull();
        metrics.ConnectionFailures.ShouldNotBeNull();
    }

    [TestMethod]
    public void Constructor_CreatesAllHistograms()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();

        // Act
        ApplicationMetrics metrics = new(meterFactory);

        // Assert
        metrics.MessageProcessingDuration.ShouldNotBeNull();
        metrics.StorageWriteDuration.ShouldNotBeNull();
        metrics.BatchSize.ShouldNotBeNull();
    }

    [TestMethod]
    public void MessagesReceived_WhenIncremented_RecordsValue()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        List<long> measurements = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName &&
                instrument.Name == "ais.receiver.messages.received")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
        });
        listener.Start();

        // Act
        metrics.MessagesReceived.Add(1);
        metrics.MessagesReceived.Add(5);

        // Assert
        measurements.ShouldContain(1);
        measurements.ShouldContain(5);
    }

    [TestMethod]
    public void SentencesReceived_WhenIncremented_RecordsValue()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        List<long> measurements = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName &&
                instrument.Name == "ais.receiver.sentences.received")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
        });
        listener.Start();

        // Act
        metrics.SentencesReceived.Add(1);
        metrics.SentencesReceived.Add(10);

        // Assert
        measurements.ShouldContain(1);
        measurements.ShouldContain(10);
    }

    [TestMethod]
    public void ErrorsReceived_WhenIncremented_RecordsValue()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        List<long> measurements = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName &&
                instrument.Name == "ais.receiver.errors")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
        });
        listener.Start();

        // Act
        metrics.ErrorsReceived.Add(1);

        // Assert
        measurements.ShouldContain(1);
    }

    [TestMethod]
    public void SetBatchesPending_UpdatesGaugeValue()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        List<long> measurements = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName &&
                instrument.Name == "ais.storage.batches.pending")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
        });
        listener.Start();

        // Act
        metrics.SetBatchesPending(100);
        listener.RecordObservableInstruments();

        // Assert
        measurements.ShouldContain(100);
    }

    [TestMethod]
    public void StorageWriteDuration_WhenRecorded_CapturesValue()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        List<double> measurements = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName &&
                instrument.Name == "ais.storage.write.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
        });
        listener.Start();

        // Act
        metrics.StorageWriteDuration.Record(42.5);

        // Assert
        measurements.ShouldContain(42.5);
    }

    [TestMethod]
    public void BatchSize_WhenRecorded_CapturesValue()
    {
        // Arrange
        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        List<long> measurements = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName &&
                instrument.Name == "ais.receiver.batch.size")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add(measurement);
        });
        listener.Start();

        // Act
        metrics.BatchSize.Record(250);

        // Assert
        measurements.ShouldContain(250);
    }

}