using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NetworkStreamNmeaReceiverTests
{
    public TestContext TestContext { get; set; } = null!;

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
    public async Task GetAsync_WhenConnected_YieldsLinesFromStream()
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

        NetworkStreamNmeaReceiver receiver = new(reader: reader, host: host, port: port, timeProvider: TimeProvider.System, retryPeriodicity: TimeSpan.FromMilliseconds(10));

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

        byte[] line = "Line1"u8.ToArray();
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line));
        reader.Reads.Enqueue(async token =>
        {
            await Task.Delay(100, token);
            return null;
        });

        NetworkStreamNmeaReceiver receiver = new(reader, host, port, TimeProvider.System, TimeSpan.FromMilliseconds(10));

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

    [TestMethod]
    public async Task GetAsync_MultipleConsecutiveFailures_EventuallySucceeds()
    {
        // Arrange
        MockNmeaStreamReader reader = new();
        string host = "localhost";
        int port = 12345;

        // First three connections fail
        reader.Connects.Enqueue(_ => throw new Exception("Failure 1"));
        reader.Connects.Enqueue(_ => throw new Exception("Failure 2"));
        reader.Connects.Enqueue(_ => throw new Exception("Failure 3"));
        // Fourth connection succeeds (default)

        byte[] line = "Success"u8.ToArray();
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line));
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(result: null));

        NetworkStreamNmeaReceiver receiver = new(reader, host, port, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        // Act
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        List<string> result = [];

        await foreach (ReadOnlyMemory<byte> item in receiver.GetAsync(cts.Token))
        {
            result.Add(System.Text.Encoding.ASCII.GetString(item.Span));
            if (result.Count >= 1)
            {
                await cts.CancelAsync();
            }
        }

        // Assert
        result.Count.ShouldBe(1);
        result[0].ShouldBe("Success");
    }

    [TestMethod]
    public void Constructor_WithAllParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        MockNmeaStreamReader reader = new();
        string host = "test.example.com";
        int port = 9876;
        TimeSpan retryPeriodicity = TimeSpan.FromSeconds(5);
        int retryAttemptLimit = 50;
        TimeSpan idleTimeout = TimeSpan.FromMinutes(2);

        // Act
        NetworkStreamNmeaReceiver receiver = new(reader, host, port, TimeProvider.System, retryPeriodicity, retryAttemptLimit, idleTimeout);

        // Assert
        receiver.Host.ShouldBe(host);
        receiver.Port.ShouldBe(port);
        receiver.RetryPeriodicity.ShouldBe(retryPeriodicity);
        receiver.RetryAttemptLimit.ShouldBe(retryAttemptLimit);
        receiver.IdleTimeout.ShouldBe(idleTimeout);
    }

    [TestMethod]
    public void Constructor_NullReader_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Should.Throw<ArgumentNullException>(() =>
            new NetworkStreamNmeaReceiver(null!, "host", 123, TimeProvider.System, TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task GetObservable_WhenDataAvailable_EmitsLinesAsObservable()
    {
        // Arrange
        MockNmeaStreamReader reader = new();
        string host = "localhost";
        int port = 12345;

        byte[] line1 = "ObservableLine1"u8.ToArray();
        byte[] line2 = "ObservableLine2"u8.ToArray();

        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line1));
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(line2));
        reader.Reads.Enqueue(_ => new ValueTask<ReadOnlyMemory<byte>?>(result: null));

        NetworkStreamNmeaReceiver receiver = new(reader, host, port, TimeProvider.System, TimeSpan.FromMilliseconds(10));

        // Act
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        List<string> result = [];

        IObservable<ReadOnlyMemory<byte>> observable = receiver.GetObservable(cts.Token);

        TaskCompletionSource tcs = new();
        using IDisposable subscription = observable.Subscribe(
            onNext: item => 
            {
                result.Add(System.Text.Encoding.ASCII.GetString(item.Span));
                if (result.Count >= 2)
                {
                    cts.Cancel();
                }
            },
            onCompleted: () => tcs.TrySetResult());

        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            // Expected when token is cancelled
        }

        // Assert
        result.Count.ShouldBe(2);
        result[0].ShouldBe("ObservableLine1");
        result[1].ShouldBe("ObservableLine2");
    }

    [TestMethod]
    public async Task DisposeAsync_WhenCalled_DisposesUnderlyingReader()
    {
        // Arrange
        bool disposed = false;
        DisposableStreamReader reader = new(() => disposed = true);
        NetworkStreamNmeaReceiver receiver = new(reader, "host", 123, TimeProvider.System, TimeSpan.FromSeconds(1));

        // Act
        await receiver.DisposeAsync();

        // Assert
        disposed.ShouldBeTrue();
    }

    private class DisposableStreamReader : INmeaStreamReader
    {
        private readonly Action onDispose;

        public DisposableStreamReader(Action onDispose)
        {
            this.onDispose = onDispose;
        }

        public bool Connected => false;

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken) => new(result: null);

        public ValueTask DisposeAsync()
        {
            this.onDispose();
            return ValueTask.CompletedTask;
        }
    }
}