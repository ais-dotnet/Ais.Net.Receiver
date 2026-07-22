// <copyright file="NetworkStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;
using System.Runtime.CompilerServices;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

public class NetworkStreamNmeaReceiver : INmeaReceiver
{
    private readonly INmeaStreamReader nmeaStreamReader;
    private readonly TimeProvider timeProvider;
    private readonly ApplicationMetrics? metrics;
    private readonly ILogger logger;

    public NetworkStreamNmeaReceiver(string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<NetworkStreamNmeaReceiver>? logger = null)
        : this(new TcpClientNmeaStreamReader(), host, port, timeProvider, retryPeriodicity, retryAttemptLimit, idleTimeout, metrics, logger)
    {
    }

    public NetworkStreamNmeaReceiver(INmeaStreamReader reader, string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<NetworkStreamNmeaReceiver>? logger = null)
    {
        this.Host = host;
        this.Port = port;
        this.timeProvider = timeProvider;
        this.RetryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(1);
        this.RetryAttemptLimit = retryAttemptLimit;
        this.IdleTimeout = idleTimeout;
        this.nmeaStreamReader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.metrics = metrics;
        this.logger = logger ?? NullLogger<NetworkStreamNmeaReceiver>.Instance;
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

        while (!cancellationToken.IsCancellationRequested)
        {
            bool connected = false;

            try
            {
                this.metrics?.ConnectionAttempts.Add(1);
                this.logger.StreamConnecting(this.Host, this.Port);
                await this.nmeaStreamReader.ConnectAsync(this.Host, this.Port, cancellationToken).ConfigureAwait(false);
                this.logger.StreamConnected(this.Host, this.Port);
                retryAttempt = 0; // Reset retry count on successful connection
                connected = true;
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this.metrics?.ConnectionFailures.Add(1);
                this.logger.StreamConnectionError(ex, this.Host, this.Port);
            }

            if (connected)
            {
                TimeSpan idleTimeout = this.IdleTimeout ?? TimeSpan.FromTicks(this.RetryPeriodicity.Ticks * this.RetryAttemptLimit);
                TimeSpan minResetInterval = idleTimeout / 2;
                long lastResetTimestamp = this.timeProvider.GetTimestamp();

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
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            // External cancellation - stop (the outer check ends the loop after disposal).
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // Read/connection error - break to reconnect.
                            this.metrics?.ConnectionFailures.Add(1);
                            this.logger.StreamConnectionError(ex, this.Host, this.Port);
                            break;
                        }

                        if (line is null)
                        {
                            // End of stream - break to reconnect.
                            break;
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
