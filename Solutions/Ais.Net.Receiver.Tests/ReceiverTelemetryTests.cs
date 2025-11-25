using System.Diagnostics.Metrics;

using Ais.Net.Receiver.Receiver;

using NSubstitute;
using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ReceiverTelemetryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Bind_RecordsMetrics_WhenEventsOccur()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        string message = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(message);
            
        // Yield 1 message
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);
        ReceiverTelemetry telemetry = new("TestMeter");
        telemetry.Bind(host);

        List<(Instrument Instrument, long Value)> measurements = [];
        using MeterListener listener = new();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "TestMeter")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add((instrument, measurement));
        });

        listener.Start();

        // Act
        await host.StartAsync(this.TestContext.CancellationTokenSource.Token);

        // Wait for processing
        await Task.Delay(100, this.TestContext.CancellationTokenSource.Token);

        // Assert
        // We expect:
        // 1 sentence received
        // 1 message received (since it's a valid single-part message)
        // 0 errors

        int sentences = measurements.Count(m => m.Instrument.Name == "ais.sentences.received");
        int messages = measurements.Count(m => m.Instrument.Name == "ais.messages.received");

        sentences.ShouldBe(1);
        messages.ShouldBe(1);
    }

    [TestMethod]
    public void Dispose_CleansUpSubscriptions()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        ReceiverHost host = new(receiver);
        ReceiverTelemetry telemetry = new("TestMeter2");
        telemetry.Bind(host);

        // Act & Assert - should not throw
        telemetry.Dispose();
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        ReceiverTelemetry telemetry = new("TestMeter3");

        // Act & Assert - should not throw
        telemetry.Dispose();
        telemetry.Dispose();
    }

    [TestMethod]
    public async Task Bind_RecordsErrors_WhenErrorsOccur()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        // "GARBAGE" causes parsing error
        string message = "GARBAGE";
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(message);

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver);
        using ReceiverTelemetry telemetry = new("TestMeter4");
        telemetry.Bind(host);

        List<(Instrument Instrument, long Value)> measurements = [];
        using MeterListener listener = new();

        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "TestMeter4")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add((instrument, measurement));
        });

        listener.Start();

        // Subscribe to messages to trigger processing
        using IDisposable sub = host.Messages.Subscribe(_ => { });

        // Act
        await host.StartAsync(this.TestContext.CancellationTokenSource.Token);
        await Task.Delay(100, this.TestContext.CancellationTokenSource.Token);

        // Assert - should have recorded errors
        int errors = measurements.Count(m => m.Instrument.Name == "ais.errors.count");
        errors.ShouldBe(1);
    }
}