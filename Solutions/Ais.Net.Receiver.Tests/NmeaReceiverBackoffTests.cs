// <copyright file="NmeaReceiverBackoffTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Logging;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// The reconnect backoff must only reset once the feed has delivered a sentence. A peer that
/// accepts the connection and then closes (or stays silent) must see the configured backoff grow,
/// not a retry per base interval forever.
/// </summary>
[TestClass]
public class NmeaReceiverBackoffTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task GetAsync_WhenPeerAcceptsThenCloses_BackoffKeepsGrowing()
    {
        // Every connection succeeds and immediately ends without a sentence.
        ScriptedConnection connection = new(linesPerConnection: 0);
        CapturingLogger logger = new();
        await using NmeaReceiver receiver = new(
            connection,
            TimeProvider.System,
            new NmeaReceiverOptions { RetryPeriodicity = TimeSpan.FromMilliseconds(1), RetryAttemptLimit = 100 },
            logger: logger);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        connection.ConnectsBeforeCancel = 4;
        connection.Cancel = cts;

        await foreach (ReadOnlyMemory<byte> _ in receiver.GetAsync(cts.Token))
        {
            Assert.Fail("No sentences were scripted.");
        }

        // With the reset on connect, every retry was attempt 1; with the reset on delivery the
        // attempt number climbs across accept-then-close cycles.
        logger.RetryAttempts.Count.ShouldBeGreaterThanOrEqualTo(3);
        logger.RetryAttempts.Take(3).ShouldBe([1, 2, 3]);
    }

    [TestMethod]
    public async Task GetAsync_WhenFeedDelivers_BackoffResets()
    {
        // Connections deliver one sentence then end, so each reconnect is a fresh attempt 1.
        ScriptedConnection connection = new(linesPerConnection: 1);
        CapturingLogger logger = new();
        await using NmeaReceiver receiver = new(
            connection,
            TimeProvider.System,
            new NmeaReceiverOptions { RetryPeriodicity = TimeSpan.FromMilliseconds(1), RetryAttemptLimit = 100 },
            logger: logger);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        connection.ConnectsBeforeCancel = 4;
        connection.Cancel = cts;

        int received = 0;
        await foreach (ReadOnlyMemory<byte> _ in receiver.GetAsync(cts.Token))
        {
            received++;
        }

        received.ShouldBeGreaterThanOrEqualTo(3);
        logger.RetryAttempts.Count.ShouldBeGreaterThanOrEqualTo(3);
        logger.RetryAttempts.Take(3).ShouldAllBe(attempt => attempt == 1);
    }

    private sealed class ScriptedConnection(int linesPerConnection) : INmeaConnection
    {
        private static readonly byte[] Line = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        private int connects;
        private int remaining;

        public int ConnectsBeforeCancel { get; set; } = int.MaxValue;

        public CancellationTokenSource? Cancel { get; set; }

        public string EndpointAddress => "scripted";

        public int EndpointPort => 0;

        public string Transport => "test";

        public string? ProtocolName => null;

        public NmeaSourceKind Kind => NmeaSourceKind.Live;

        public bool Connected { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            this.connects++;
            if (this.connects > this.ConnectsBeforeCancel)
            {
                this.Cancel?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            this.remaining = linesPerConnection;
            this.Connected = true;
            return Task.CompletedTask;
        }

        public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (this.remaining-- > 0)
            {
                return new ValueTask<ReadOnlyMemory<byte>?>(Line);
            }

            this.Connected = false;
            return new ValueTask<ReadOnlyMemory<byte>?>(result: null);
        }

        public ValueTask DisposeAsync()
        {
            this.Connected = false;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Records the attempt number carried by each StreamConnectionRetry log event.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<int> RetryAttempts { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != "StreamConnectionRetry" || state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return;
            }

            foreach (KeyValuePair<string, object?> value in values)
            {
                if (string.Equals(value.Key, "attempt", StringComparison.OrdinalIgnoreCase) && value.Value is int attempt)
                {
                    lock (this.RetryAttempts)
                    {
                        this.RetryAttempts.Add(attempt);
                    }
                }
            }
        }
    }
}
