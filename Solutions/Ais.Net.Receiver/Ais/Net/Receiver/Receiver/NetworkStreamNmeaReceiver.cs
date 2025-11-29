// <copyright file="NetworkStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Ais.Net.Receiver.Telemetry;

namespace Ais.Net.Receiver.Receiver;

public class NetworkStreamNmeaReceiver : INmeaReceiver
{
    private readonly INmeaStreamReader nmeaStreamReader;
    private readonly TimeProvider timeProvider;
    private readonly ApplicationMetrics? metrics;

    public NetworkStreamNmeaReceiver(string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null)
        : this(new TcpClientNmeaStreamReader(), host, port, timeProvider, retryPeriodicity, retryAttemptLimit, idleTimeout, metrics)
    {
    }

    public NetworkStreamNmeaReceiver(INmeaStreamReader reader, string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null)
    {
        this.Host = host;
        this.Port = port;
        this.timeProvider = timeProvider;
        this.RetryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(1);
        this.RetryAttemptLimit = retryAttemptLimit;
        this.IdleTimeout = idleTimeout;
        this.nmeaStreamReader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.metrics = metrics;
    }

    public string Host { get; }
    
    public int Port { get; }
    
    public int RetryAttemptLimit { get; }
    
    public TimeSpan RetryPeriodicity { get; }

    public TimeSpan? IdleTimeout { get; }

    // We still provide the IAsyncEnumerable API for backwards compatibility.
    public IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default) => this.GetObservable(cancellationToken).ToAsyncEnumerable();

    public IObservable<ReadOnlyMemory<byte>> GetObservable(CancellationToken cancellationToken = default)
    {
        IObservable<ReadOnlyMemory<byte>> withoutRetry = Observable.Create<ReadOnlyMemory<byte>>(async (obs, innerCancel) =>
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancel);
            CancellationToken mergedToken = cts.Token;

            int retryAttempt = 0;

            while (!mergedToken.IsCancellationRequested)
            {
                try
                {
                    this.metrics?.ConnectionAttempts.Add(1);
                    await this.nmeaStreamReader.ConnectAsync(this.Host, this.Port, mergedToken).ConfigureAwait(false);
                    retryAttempt = 0; // Reset retry count on successful connection

                    try
                    {
                        TimeSpan idleTimeout = this.IdleTimeout ?? TimeSpan.FromTicks(this.RetryPeriodicity.Ticks * this.RetryAttemptLimit);
                        TimeSpan minResetInterval = idleTimeout / 2;
                        long lastResetTimestamp = this.timeProvider.GetTimestamp();

                        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(mergedToken);
                        timeoutCts.CancelAfter(idleTimeout);

                        while (this.nmeaStreamReader.Connected && !mergedToken.IsCancellationRequested)
                        {
                            try
                            {
                                ReadOnlyMemory<byte>? line = await this.nmeaStreamReader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                                if (line is not null)
                                {
                                    obs.OnNext(line.Value);

                                    TimeSpan elapsed = this.timeProvider.GetElapsedTime(lastResetTimestamp);
                                    if (elapsed > minResetInterval)
                                    {
                                        timeoutCts.CancelAfter(idleTimeout);
                                        lastResetTimestamp = this.timeProvider.GetTimestamp();
                                    }
                                }
                                else
                                {
                                    // End of stream, break to reconnect
                                    break;
                                }
                            }
                            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !mergedToken.IsCancellationRequested)
                            {
                                // Idle timeout, break to reconnect
                                break;
                            }
                        }
                    }
                    finally
                    {
                        await this.nmeaStreamReader.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Connection failed, fall through to retry logic
                    this.metrics?.ConnectionFailures.Add(1);
                }

                if (mergedToken.IsCancellationRequested)
                {
                    break;
                }

                // Calculate backoff (Linear backoff based on RetryPeriodicity)
                retryAttempt++;
                int cappedRetryAttempt = Math.Min(retryAttempt, this.RetryAttemptLimit);
                TimeSpan delay = TimeSpan.FromTicks(this.RetryPeriodicity.Ticks * cappedRetryAttempt);
                
                try
                {
                    await Task.Delay(delay, mergedToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        return withoutRetry.Retry();
    }

    public async ValueTask DisposeAsync()
    {
        await this.nmeaStreamReader.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}