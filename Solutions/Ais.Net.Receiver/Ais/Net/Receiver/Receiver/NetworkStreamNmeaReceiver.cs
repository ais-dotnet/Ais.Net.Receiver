// <copyright file="NetworkStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ais.Net.Receiver.Receiver;

public class NetworkStreamNmeaReceiver : INmeaReceiver, IAsyncDisposable
{
    private readonly INmeaStreamReader nmeaStreamReader;

    public NetworkStreamNmeaReceiver(string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null)
        : this(new TcpClientNmeaStreamReader(), host, port, retryPeriodicity, retryAttemptLimit, idleTimeout)
    {
    }

    public NetworkStreamNmeaReceiver(INmeaStreamReader reader, string host, int port, TimeSpan? retryPeriodicity, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null)
    {
        this.Host = host;
        this.Port = port;
        this.RetryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(1);
        this.RetryAttemptLimit = retryAttemptLimit;
        this.IdleTimeout = idleTimeout;
        this.nmeaStreamReader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public string Host { get; }
    
    public int Port { get; }
    
    public int RetryAttemptLimit { get; }
    
    public TimeSpan RetryPeriodicity { get; }

    public TimeSpan? IdleTimeout { get; }

    // We still provide the IAsyncEnumerable API for backwards compatibility.
    public IAsyncEnumerable<string> GetAsync(CancellationToken cancellationToken = default) => this.GetObservable(cancellationToken).ToAsyncEnumerable();

    public IObservable<string> GetObservable(CancellationToken cancellationToken = default)
    {
        IObservable<string> withoutRetry = Observable.Create<string>(async (obs, innerCancel) =>
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancel);
            CancellationToken mergedToken = cts.Token;

            int retryAttempt = 0;

            while (!mergedToken.IsCancellationRequested)
            {
                try
                {
                    await this.nmeaStreamReader.ConnectAsync(this.Host, this.Port, mergedToken).ConfigureAwait(false);
                    retryAttempt = 0; // Reset retry count on successful connection

                    try
                    {
                        TimeSpan idleTimeout = this.IdleTimeout ?? TimeSpan.FromTicks(this.RetryPeriodicity.Ticks * this.RetryAttemptLimit);
                        long idleTimeoutMs = (long)idleTimeout.TotalMilliseconds;
                        long minResetIntervalMs = idleTimeoutMs / 2;
                        long lastResetMs = Environment.TickCount64;

                        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(mergedToken);
                        timeoutCts.CancelAfter(idleTimeout);

                        while (this.nmeaStreamReader.Connected && !mergedToken.IsCancellationRequested)
                        {
                            try
                            {
                                string? line = await this.nmeaStreamReader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                                if (line is not null)
                                {
                                    obs.OnNext(line);
                                    
                                    long now = Environment.TickCount64;
                                    if (now - lastResetMs > minResetIntervalMs)
                                    {
                                        timeoutCts.CancelAfter(idleTimeout);
                                        lastResetMs = now;
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
        if (this.nmeaStreamReader is not null)
        {
            await this.nmeaStreamReader.DisposeAsync().ConfigureAwait(false);
        }
    }
}