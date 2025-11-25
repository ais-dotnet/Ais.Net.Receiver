using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NetworkStreamNmeaReceiverTests
{
    private class MockNmeaStreamReader : INmeaStreamReader
    {
        public bool Connected { get; set; } = true;

        public Queue<Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>> Reads { get; } = new();

        public Queue<Func<CancellationToken, Task>> Connects { get; } = new();

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) =>
            this.Connects.TryDequeue(out Func<CancellationToken, Task>? func) ? func(cancellationToken) : Task.CompletedTask;

        public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
        {
            return this.Reads.TryDequeue(out Func<CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>? func) ? func(cancellationToken) :
                // Return null (end of stream) by default
                new ValueTask<ReadOnlyMemory<byte>?>(result: null);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    public async Task GetAsync_ConnectsAndReadsLines()
    {
        // Arrange
        MockNmeaStreamReader reader = new();
        string host = "localhost";
        int port = 12345;

        byte[] line1 = "Line1"u8.ToArray();
        byte[] line2 = "Line2"u8.ToArray();

        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line1));
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line2));
        reader.Reads.Enqueue(async token =>
        {
            // Simulate delay before end of stream to allow cancellation
            await Task.Delay(100, token);
            return null;
        });

        NetworkStreamNmeaReceiver receiver = new(reader, host, port, TimeSpan.FromMilliseconds(10));

        // Act
        using CancellationTokenSource cts = new();
        List<string> result = [];

        try
        {
            await foreach (ReadOnlyMemory<byte> item in receiver.GetAsync(cts.Token))
            {
                result.Add(System.Text.Encoding.ASCII.GetString(item.Span));

                if (result.Count >= 2)
                {
                    await cts.CancelAsync();
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
        MockNmeaStreamReader reader = new();
        string host = "localhost";
        int port = 12345;

        // First connect fails
        reader.Connects.Enqueue(_ => throw new Exception("Connection failed"));
        // Second connect succeeds (default)

        byte[] line = System.Text.Encoding.ASCII.GetBytes("Line1");
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line));
        reader.Reads.Enqueue(async token =>
        {
            await Task.Delay(100, token);
            return null;
        });

        NetworkStreamNmeaReceiver receiver = new(reader, host, port, TimeSpan.FromMilliseconds(10));

        // Act
        using CancellationTokenSource cts = new();
        List<string> result = [];

        try
        {
            await foreach (ReadOnlyMemory<byte> item in receiver.GetAsync(cts.Token))
            {
                result.Add(System.Text.Encoding.ASCII.GetString(item.Span));
                await cts.CancelAsync(); // Stop after first success
            }
        }
        catch (OperationCanceledException)
        {
        }

        // Assert
        result.Count.ShouldBe(1);
        result[0].ShouldBe("Line1");
    }
}