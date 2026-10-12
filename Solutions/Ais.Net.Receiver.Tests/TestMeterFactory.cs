using System.Diagnostics.Metrics;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Simple <see cref="IMeterFactory"/> implementation for testing.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> meters = [];

    /// <summary>
    /// Gets the meters this factory has created, so a listener can filter by meter identity rather
    /// than by name - see <see cref="MeterCollector"/>.
    /// </summary>
    public IReadOnlyCollection<Meter> Meters => this.meters;

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
