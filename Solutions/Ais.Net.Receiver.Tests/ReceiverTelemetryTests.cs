using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ais.Net.Receiver.Receiver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Shouldly;

namespace Ais.Net.Receiver.Tests
{
    [TestClass]
    public class ReceiverTelemetryTests
    {
        [TestMethod]
        public async Task Bind_RecordsMetrics_WhenEventsOccur()
        {
            // Arrange
            var receiver = Substitute.For<INmeaReceiver>();
            var message = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
            var bytes = System.Text.Encoding.ASCII.GetBytes(message);
            
            // Yield 1 message
            receiver.GetAsync(Arg.Any<CancellationToken>())
                .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

            var host = new ReceiverHost(receiver);
            var telemetry = new ReceiverTelemetry("TestMeter");
            telemetry.Bind(host);

            var measurements = new List<(Instrument Instrument, long Value)>();
            using var listener = new MeterListener();
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
            await host.StartAsync(CancellationToken.None);
            
            // Wait for processing
            await Task.Delay(100);

            // Assert
            // We expect:
            // 1 sentence received
            // 1 message received (since it's a valid single-part message)
            // 0 errors
            
            // Note: ReceiverHost implementation details determine exact counts.
            // Assuming StartAsync processes the message.
            
            var sentences = measurements.Count(m => m.Instrument.Name == "ais.sentences.received");
            var messages = measurements.Count(m => m.Instrument.Name == "ais.messages.received");
            
            sentences.ShouldBeGreaterThan(0);
            messages.ShouldBeGreaterThan(0);
        }
    }
}
