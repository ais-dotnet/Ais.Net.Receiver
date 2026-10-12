// <copyright file="MeterCollector.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics.Metrics;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Collects counter measurements from a specific set of <see cref="Meter"/> instances.
/// </summary>
/// <remarks>
/// Filtering is by meter <em>instance</em> rather than by name, and that is the whole point of this
/// type. <see cref="MeterListener"/> is process-wide and this assembly runs tests in parallel at
/// method level, so several tests can hold their own <see cref="Telemetry.ApplicationMetrics"/> - all
/// sharing one meter name - at the same time. A name filter would silently pool their measurements
/// together and make any assertion on a total depend on what else happened to be running.
/// </remarks>
internal sealed class MeterCollector : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly Dictionary<string, long> totals = [];

    private MeterCollector(IReadOnlyCollection<Meter> meters)
    {
        this.listener.InstrumentPublished = (instrument, l) =>
        {
            if (meters.Any(meter => ReferenceEquals(meter, instrument.Meter)))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        this.listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            lock (this.totals)
            {
                this.totals[instrument.Name] = this.totals.GetValueOrDefault(instrument.Name) + measurement;
            }
        });

        this.listener.Start();
    }

    /// <summary>
    /// Starts collecting from every meter the supplied factory has created, and any it creates later.
    /// </summary>
    /// <param name="factory">The factory whose meters should be observed.</param>
    /// <returns>A collector; dispose it to stop listening.</returns>
    internal static MeterCollector ForMeters(TestMeterFactory factory) => new(factory.Meters);

    /// <summary>
    /// Gets the summed measurements recorded for an instrument, or zero when it never reported.
    /// </summary>
    /// <param name="instrumentName">The instrument name, e.g. <c>ais.storage.batches.failed</c>.</param>
    /// <returns>The total.</returns>
    internal long Total(string instrumentName)
    {
        // Observable instruments only report when polled, and counters are recorded as they happen;
        // this forces any pending observations before the total is read.
        this.listener.RecordObservableInstruments();

        lock (this.totals)
        {
            return this.totals.GetValueOrDefault(instrumentName);
        }
    }

    public void Dispose() => this.listener.Dispose();
}
