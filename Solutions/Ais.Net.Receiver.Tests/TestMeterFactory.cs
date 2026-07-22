using System.Diagnostics.Metrics;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Simple <see cref="IMeterFactory"/> implementation for testing.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> meters = [];

    public Meter Create(MeterOptions options)
    {
        Meter meter = new(options);
        this.meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (Meter meter in this.meters)
        {
            meter.Dispose();
        }
    }
}
