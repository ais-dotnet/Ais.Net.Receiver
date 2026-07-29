// <copyright file="NetworkStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

public class NetworkStreamNmeaReceiver : INmeaReceiver
{
    // Fallback idle-read timeout when none is configured. Detects a silently dropped (half-open) TCP
    // connection without being so short that a legitimately quiet feed churns reconnects. Deliberately
    // independent of the reconnect knobs, which are unrelated and (with the shipped 1s x 5) would
    // otherwise make this only ~5 seconds.
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(60);

    private readonly INmeaStreamReader nmeaStreamReader;
    private readonly TimeProvider timeProvider;
    private readonly ApplicationMetrics? metrics;
    private readonly ApplicationInstrumentation? instrumentation;
    private readonly ILogger logger;
    private readonly Action<bool>? onConnectionStateChanged;

    public NetworkStreamNmeaReceiver(string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<NetworkStreamNmeaReceiver>? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null)
        : this(new TcpClientNmeaStreamReader(), host, port, timeProvider, retryPeriodicity, retryAttemptLimit, idleTimeout, metrics, logger, onConnectionStateChanged, instrumentation)
    {
    }

    public NetworkStreamNmeaReceiver(INmeaStreamReader reader, string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<NetworkStreamNmeaReceiver>? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null)
    {
        this.Host = host;
        this.Port = port;
        this.timeProvider = timeProvider;
        this.RetryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(1);
        this.RetryAttemptLimit = retryAttemptLimit;
        this.IdleTimeout = idleTimeout;
        this.nmeaStreamReader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.metrics = metrics;
        this.instrumentation = instrumentation;
        this.logger = logger ?? NullLogger<NetworkStreamNmeaReceiver>.Instance;

        // Reports the live TCP connection state (true on connect, false on any disconnect/idle/error),
        // so health reflects the real socket rather than the host's start/stop lifetime.
        this.onConnectionStateChanged = onConnectionStateChanged;
    }

    public string Host { get; }

    public int Port { get; }

    public int RetryAttemptLimit { get; }

    public TimeSpan RetryPeriodicity { get; }

    public TimeSpan? IdleTimeout { get; }

    /// <summary>
    /// Streams NMEA lines from the network, reconnecting with linear backoff on failure or idle
    /// timeout. This is a native pull-based sequence: a yielded buffer is only guaranteed valid
    /// until the next <c>MoveNextAsync</c> (the underlying reader reuses its read buffer), which is
    /// safe here because each line is consumed synchronously before the next is requested.
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int retryAttempt = 0;

        // Mirrors retryAttempt into the consecutive-failure gauge. Tracked separately so the gauge
        // can be wound back to zero on connect without assuming what it currently reads.
        long reportedConsecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            bool connected = false;

            // A span per connection attempt: this is the low-frequency, high-value event in this loop,
            // and it gives the connect/disconnect transitions somewhere to live.
            using (Activity? connectActivity = this.instrumentation?.ActivitySource.StartActivity("Connect"))
            {
                connectActivity?.SetTag(SemanticConventions.Network.ServerAddress, this.Host);
                connectActivity?.SetTag(SemanticConventions.Network.ServerPort, this.Port);
                connectActivity?.SetTag(SemanticConventions.Network.Transport, "tcp");
                connectActivity?.SetTag(SemanticConventions.Connection.Attempt, retryAttempt);

                try
                {
                    this.metrics?.ConnectionAttempts.Add(1);
                    this.logger.StreamConnecting(this.Host, this.Port);
                    await this.nmeaStreamReader.ConnectAsync(this.Host, this.Port, cancellationToken).ConfigureAwait(false);
                    this.logger.StreamConnected(this.Host, this.Port);
                    retryAttempt = 0; // Reset retry count on successful connection

                    // Note: the consecutive-failure gauge is deliberately NOT reset here. Connecting
                    // proves the port accepts, not that the feed sends; it is reset on the first
                    // delivered sentence instead.
                    connected = true;
                    connectActivity?.RecordConnectionStateEvent(true, "connect_succeeded", this.Host, this.Port);
                    this.onConnectionStateChanged?.Invoke(true);
                }
                catch (OperationCanceledException)
                {
                    connectActivity?.RecordConnectionStateEvent(false, "shutdown", this.Host, this.Port);
                    yield break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    this.metrics?.ConnectionFailures.Add(1);
                    this.metrics?.ConsecutiveConnectionFailures.Add(1);
                    reportedConsecutiveFailures++;
                    this.logger.StreamConnectionError(ex, this.Host, this.Port);
                    connectActivity?.RecordExceptionWithStatus(ex, escaped: false);
                    connectActivity?.SetErrorType("connect_failed");
                    connectActivity?.RecordConnectionStateEvent(false, "connect_failed", this.Host, this.Port);
                    this.onConnectionStateChanged?.Invoke(false);
                }
            }

            if (connected)
            {
                TimeSpan idleTimeout = this.IdleTimeout is { } configured && configured > TimeSpan.Zero
                    ? configured
                    : DefaultIdleTimeout;
                TimeSpan minResetInterval = idleTimeout / 2;
                long lastResetTimestamp = this.timeProvider.GetTimestamp();

                // A successful connect is not evidence of a working feed: a server that accepts and
                // then immediately closes, or accepts and stays silent, reconnects cleanly forever
                // while delivering nothing. Health is therefore tracked against delivered data, not
                // against connect success.
                bool deliveredData = false;
                bool failureCounted = false;
                string endReason = "stream_ended";

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(idleTimeout);

                try
                {
                    while (this.nmeaStreamReader.Connected && !cancellationToken.IsCancellationRequested)
                    {
                        ReadOnlyMemory<byte>? line;
                        try
                        {
                            line = await this.nmeaStreamReader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            // Idle timeout - break to reconnect.
                            this.logger.StreamIdleTimeout(idleTimeout.TotalSeconds, this.Host, this.Port);
                            endReason = "idle_timeout";
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            // External cancellation - stop (the outer check ends the loop after disposal).
                            endReason = "shutdown";
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // Read/connection error - break to reconnect.
                            this.metrics?.ConnectionFailures.Add(1);
                            failureCounted = true;
                            this.logger.StreamConnectionError(ex, this.Host, this.Port);
                            endReason = "read_error";
                            break;
                        }

                        if (line is null)
                        {
                            // End of stream - break to reconnect.
                            break;
                        }

                        if (!deliveredData)
                        {
                            // First sentence of this connection: the feed is demonstrably live, so
                            // wind the consecutive-failure gauge back to zero.
                            deliveredData = true;

                            if (reportedConsecutiveFailures > 0)
                            {
                                this.metrics?.ConsecutiveConnectionFailures.Add(-reportedConsecutiveFailures);
                                reportedConsecutiveFailures = 0;
                            }
                        }

                        yield return line.Value;

                        TimeSpan elapsed = this.timeProvider.GetElapsedTime(lastResetTimestamp);
                        if (elapsed > minResetInterval)
                        {
                            timeoutCts.CancelAfter(idleTimeout);
                            lastResetTimestamp = this.timeProvider.GetTimestamp();
                        }
                    }
                }
                finally
                {
                    await this.nmeaStreamReader.DisposeAsync().ConfigureAwait(false);

                    // A connection that ended without ever yielding a sentence is an outage, even
                    // though connect succeeded. Count it so the consecutive-failure gauge - and the
                    // alert built on it - sees accept-then-close and accept-then-silent feeds.
                    // Cancellation is excluded: that is a shutdown, not a failure.
                    if (!deliveredData && !cancellationToken.IsCancellationRequested)
                    {
                        if (!failureCounted)
                        {
                            this.metrics?.ConnectionFailures.Add(1);
                        }

                        this.metrics?.ConsecutiveConnectionFailures.Add(1);
                        reportedConsecutiveFailures++;
                        this.logger.StreamConnectedButSilent(this.Host, this.Port);

                        if (endReason == "stream_ended")
                        {
                            endReason = "connected_but_silent";
                        }
                    }

                    // Record the closing transition with why it happened, so a trace shows whether the
                    // feed ended cleanly, stalled, errored, or was shut down.
                    using (Activity? closeActivity = this.instrumentation?.ActivitySource.StartActivity("ConnectionClosed"))
                    {
                        closeActivity?.RecordConnectionStateEvent(false, endReason, this.Host, this.Port);
                    }

                    // The connection ended (disconnect, idle timeout, read error, or cancellation).
                    this.onConnectionStateChanged?.Invoke(false);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Linear backoff based on RetryPeriodicity.
            retryAttempt++;
            int cappedRetryAttempt = Math.Min(retryAttempt, this.RetryAttemptLimit);
            TimeSpan delay = TimeSpan.FromTicks(this.RetryPeriodicity.Ticks * cappedRetryAttempt);

            this.metrics?.RetryAttempts.Add(
                1,
                new KeyValuePair<string, object?>("component", "connection"));

            this.logger.StreamConnectionRetry(retryAttempt, this.Host, this.Port, (long)delay.TotalMilliseconds);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Backwards-compatible observable projection over <see cref="GetAsync"/>. Each line is copied
    /// so that observers - whose lifetimes this method does not control - receive an owned buffer.
    /// </summary>
    public IObservable<ReadOnlyMemory<byte>> GetObservable(CancellationToken cancellationToken = default) =>
        Observable.Create<ReadOnlyMemory<byte>>(async (observer, innerCancellation) =>
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancellation);
            try
            {
                await foreach (ReadOnlyMemory<byte> line in this.GetAsync(cts.Token).ConfigureAwait(false))
                {
                    observer.OnNext(line.ToArray());
                }

                observer.OnCompleted();
            }
            catch (OperationCanceledException)
            {
                observer.OnCompleted();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                observer.OnError(ex);
            }
        });

    public async ValueTask DisposeAsync()
    {
        await this.nmeaStreamReader.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
