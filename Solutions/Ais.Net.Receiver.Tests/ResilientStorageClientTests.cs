using System.Diagnostics.Metrics;
using System.Text;

using Ais.Net.Receiver.Storage;
using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ResilientStorageClientTests
{
    private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(1);

    [TestMethod]
    public async Task PersistAsync_RetriesUntilSuccess()
    {
        IStorageClient inner = Substitute.For<IStorageClient>();
        int calls = 0;
        inner.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>())
            .Returns(_ =>
            {
                calls++;
                return calls < 3 ? Task.FromException(new IOException("transient")) : Task.CompletedTask;
            });

        using ResilientStorageClient client = new(inner, TimeProvider.System, maxAttempts: 3, FastRetry);

        await client.PersistAsync(Batch("!AIVDM,1,1,,A,x,0*00"));

        calls.ShouldBe(3);
    }

    [TestMethod]
    public async Task PersistAsync_WhenRetriesExhaustedWithoutDeadLetter_ThrowsAndCountsFailure()
    {
        IStorageClient inner = Substitute.For<IStorageClient>();
        inner.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>())
            .Returns(_ => Task.FromException(new IOException("down")));

        using TestMeterFactory meterFactory = new();
        ApplicationMetrics metrics = new(meterFactory);
        long failures = 0;
        using MeterListener listener = ListenTo("ais.storage.batches.failed", value => failures += value);

        using ResilientStorageClient client = new(inner, TimeProvider.System, maxAttempts: 2, FastRetry, deadLetterPath: null, metrics);

        await Should.ThrowAsync<IOException>(() => client.PersistAsync(Batch("!AIVDM,1,1,,A,x,0*00")));
        failures.ShouldBe(1);
    }

    [TestMethod]
    public async Task PersistAsync_WhenRetriesExhaustedWithDeadLetter_WritesBatchAndDoesNotThrow()
    {
        IStorageClient inner = Substitute.For<IStorageClient>();
        inner.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>())
            .Returns(_ => Task.FromException(new IOException("down")));

        string deadLetterDir = Path.Combine(Path.GetTempPath(), "ais-deadletter-" + Guid.NewGuid().ToString("N"));
        try
        {
            using ResilientStorageClient client = new(inner, TimeProvider.System, maxAttempts: 2, FastRetry, deadLetterDir);

            await client.PersistAsync(Batch("SENTENCE-A", "SENTENCE-B"));

            string[] files = Directory.GetFiles(deadLetterDir);
            files.Length.ShouldBe(1);
            (await File.ReadAllTextAsync(files[0])).ShouldBe("SENTENCE-A\nSENTENCE-B\n");
        }
        finally
        {
            if (Directory.Exists(deadLetterDir))
            {
                Directory.Delete(deadLetterDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task PersistAsync_WhenDeadLettered_LogsTheExceptionThatExhaustedTheRetries()
    {
        IOException cause = new("storage account not found");
        IStorageClient inner = Substitute.For<IStorageClient>();
        inner.PersistAsync(Arg.Any<IEnumerable<ReadOnlyMemory<byte>>>()).Returns(_ => Task.FromException(cause));

        CapturingLogger logger = new();
        using TempDirectory deadLetterDir = new();
        using ResilientStorageClient client = new(
            inner, TimeProvider.System, maxAttempts: 2, FastRetry, deadLetterDir.Path, metrics: null, logger);

        await client.PersistAsync(Batch("SENTENCE-A"));

        // The real cause has to reach the log, otherwise the dead-letter entry says nothing about
        // whether storage was unreachable, throttling, or rejecting our credentials.
        logger.Exceptions.ShouldHaveSingleItem().ShouldBeSameAs(cause);
    }

    [TestMethod]
    public async Task Dispose_DisposesInner()
    {
        IStorageClient inner = Substitute.For<IStorageClient>();
        ResilientStorageClient client = new(inner, TimeProvider.System, maxAttempts: 1, FastRetry);

        client.Dispose();

        inner.Received(1).Dispose();
    }

    private static ReadOnlyMemory<byte>[] Batch(params string[] lines) =>
        [.. lines.Select(line => (ReadOnlyMemory<byte>)Encoding.ASCII.GetBytes(line))];

    private static MeterListener ListenTo(string instrumentName, Action<long> onMeasurement)
    {
        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ApplicationMetrics.MeterName && instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => onMeasurement(measurement));
        listener.Start();
        return listener;
    }

    /// <summary>Records the exceptions attached to log entries.</summary>
    private sealed class CapturingLogger : ILogger
    {
        private readonly List<Exception> exceptions = [];

        public IReadOnlyList<Exception> Exceptions => this.exceptions;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                this.exceptions.Add(exception);
            }
        }
    }
}
