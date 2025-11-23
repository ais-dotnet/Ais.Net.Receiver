using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ais.Net.Receiver.Receiver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Ais.Net.Receiver.Tests
{
    [TestClass]
    public class NetworkStreamNmeaReceiverTests
    {
        private class MockNmeaStreamReader : INmeaStreamReader
        {
            public bool Connected { get; set; } = true;
            public Queue<Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>> Reads { get; } = new();
            public Queue<Func<CancellationToken, Task>> Connects { get; } = new();

            public Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
            {
                if (Connects.TryDequeue(out var func))
                {
                    return func(cancellationToken);
                }
                return Task.CompletedTask;
            }

            public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
            {
                if (Reads.TryDequeue(out var func))
                {
                    return func(cancellationToken);
                }
                // Return null (end of stream) by default
                return new ValueTask<ReadOnlyMemory<byte>?>(result: null);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        [TestMethod]
        public async Task GetAsync_ConnectsAndReadsLines()
        {
            // Arrange
            var reader = new MockNmeaStreamReader();
            var host = "localhost";
            var port = 12345;
            
            var line1 = System.Text.Encoding.ASCII.GetBytes("Line1");
            var line2 = System.Text.Encoding.ASCII.GetBytes("Line2");

            reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line1));
            reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line2));
            reader.Reads.Enqueue(async token => {
                // Simulate delay before end of stream to allow cancellation
                await Task.Delay(100, token);
                return null;
            });

            var receiver = new NetworkStreamNmeaReceiver(reader, host, port, TimeSpan.FromMilliseconds(10));

            // Act
            using var cts = new CancellationTokenSource();
            var result = new List<string>();
            
            try
            {
                await foreach (var item in receiver.GetAsync(cts.Token))
                {
                    result.Add(System.Text.Encoding.ASCII.GetString(item.Span));
                    if (result.Count >= 2)
                    {
                        cts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Assert
            result.Count.ShouldBe(2);
            result[0].ShouldBe("Line1");
            result[1].ShouldBe("Line2");
        }

        [TestMethod]
        public async Task GetAsync_RetriesOnConnectionFailure()
        {
            // Arrange
            var reader = new MockNmeaStreamReader();
            var host = "localhost";
            var port = 12345;
            
            // First connect fails
            reader.Connects.Enqueue(_ => throw new Exception("Connection failed"));
            // Second connect succeeds (default)
            
            var line = System.Text.Encoding.ASCII.GetBytes("Line1");
            reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line));
            reader.Reads.Enqueue(async token => {
                await Task.Delay(100, token);
                return null;
            });

            var receiver = new NetworkStreamNmeaReceiver(reader, host, port, TimeSpan.FromMilliseconds(10));

            // Act
            using var cts = new CancellationTokenSource();
            var result = new List<string>();
            
            try
            {
                await foreach (var item in receiver.GetAsync(cts.Token))
                {
                    result.Add(System.Text.Encoding.ASCII.GetString(item.Span));
                    cts.Cancel(); // Stop after first success
                }
            }
            catch (OperationCanceledException) { }

            // Assert
            result.Count.ShouldBe(1);
            result[0].ShouldBe("Line1");
        }
    }
}
