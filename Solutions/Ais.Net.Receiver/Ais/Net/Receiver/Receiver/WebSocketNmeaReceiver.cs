// <copyright file="WebSocketNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net.WebSockets;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Receives NMEA sentences from a WebSocket (<c>ws://</c> or <c>wss://</c>) feed, reconnecting with
/// linear backoff on failure, peer close, or idle timeout.
/// </summary>
public class WebSocketNmeaReceiver : INmeaReceiver
{
    private readonly NmeaReceiver core;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketNmeaReceiver"/> class that connects
    /// with a <see cref="WebSocketNmeaConnection"/>.
    /// </summary>
    /// <param name="url">The absolute <c>ws://</c> or <c>wss://</c> URL of the feed.</param>
    /// <param name="timeProvider">The time provider used for idle-timeout bookkeeping.</param>
    /// <param name="retryPeriodicity">The base reconnect delay; defaults to one second.</param>
    /// <param name="retryAttemptLimit">The attempt count at which the linear backoff stops growing.</param>
    /// <param name="idleTimeout">How long the feed may be silent before reconnecting.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="logger">An optional logger.</param>
    /// <param name="onConnectionStateChanged">Optional callback reporting the live connection state.</param>
    /// <param name="instrumentation">Optional tracing instrumentation.</param>
    /// <param name="configureOptions">Optional callback that configures each <see cref="ClientWebSocket"/> before it connects.</param>
    public WebSocketNmeaReceiver(Uri url, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<WebSocketNmeaReceiver>? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null, Action<ClientWebSocketOptions>? configureOptions = null)
        : this(new WebSocketNmeaConnection(url, null, configureOptions), url, timeProvider, retryPeriodicity, retryAttemptLimit, idleTimeout, metrics, logger, onConnectionStateChanged, instrumentation)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketNmeaReceiver"/> class over a supplied
    /// connection.
    /// </summary>
    /// <param name="connection">The connection to read from.</param>
    /// <param name="url">The URL of the feed, as reported by <see cref="Url"/>.</param>
    /// <param name="timeProvider">The time provider used for idle-timeout bookkeeping.</param>
    /// <param name="retryPeriodicity">The base reconnect delay; defaults to one second.</param>
    /// <param name="retryAttemptLimit">The attempt count at which the linear backoff stops growing.</param>
    /// <param name="idleTimeout">How long the feed may be silent before reconnecting.</param>
    /// <param name="metrics">Optional application metrics.</param>
    /// <param name="logger">An optional logger.</param>
    /// <param name="onConnectionStateChanged">Optional callback reporting the live connection state.</param>
    /// <param name="instrumentation">Optional tracing instrumentation.</param>
    public WebSocketNmeaReceiver(INmeaConnection connection, Uri url, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<WebSocketNmeaReceiver>? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        this.Url = url ?? throw new ArgumentNullException(nameof(url));

        // The connection-state callback reports the live WebSocket state (true on connect, false on
        // any disconnect/idle/error), so health reflects the real connection rather than the host's
        // start/stop lifetime.
        this.core = new NmeaReceiver(
            connection,
            timeProvider,
            new NmeaReceiverOptions
            {
                RetryPeriodicity = retryPeriodicity ?? TimeSpan.FromSeconds(1),
                RetryAttemptLimit = retryAttemptLimit,
                IdleTimeout = idleTimeout,
            },
            metrics,
            logger,
            onConnectionStateChanged,
            instrumentation);
    }

    /// <summary>
    /// Gets the URL of the feed.
    /// </summary>
    public Uri Url { get; }

    /// <summary>
    /// Gets the attempt count at which the linear reconnect backoff stops growing.
    /// </summary>
    public int RetryAttemptLimit => this.core.Options.RetryAttemptLimit;

    /// <summary>
    /// Gets the base reconnect delay.
    /// </summary>
    public TimeSpan RetryPeriodicity => this.core.Options.RetryPeriodicity;

    /// <summary>
    /// Gets the configured idle timeout, if any.
    /// </summary>
    public TimeSpan? IdleTimeout => this.core.Options.IdleTimeout;

    /// <summary>
    /// Streams NMEA lines from the WebSocket feed, reconnecting with linear backoff on failure or idle
    /// timeout. This is a native pull-based sequence: a yielded buffer is only guaranteed valid
    /// until the next <c>MoveNextAsync</c> (the underlying connection reuses its line buffer), which
    /// is safe here because each line is consumed synchronously before the next is requested.
    /// </summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>The sequence of NMEA lines.</returns>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default) =>
        this.core.GetAsync(cancellationToken);

    /// <summary>
    /// Backwards-compatible observable projection over <see cref="GetAsync"/>. Each line is copied
    /// so that observers - whose lifetimes this method does not control - receive an owned buffer.
    /// </summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>An observable of NMEA lines.</returns>
    public IObservable<ReadOnlyMemory<byte>> GetObservable(CancellationToken cancellationToken = default) =>
        this.core.GetObservable(cancellationToken);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.core.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
