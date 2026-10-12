// <copyright file="NetworkStreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Receiver;

public class NetworkStreamNmeaReceiver : INmeaReceiver
{
    private readonly NmeaReceiver core;

    public NetworkStreamNmeaReceiver(string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<NetworkStreamNmeaReceiver>? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null)
        : this(new TcpNmeaConnection(host, port), timeProvider, retryPeriodicity, retryAttemptLimit, idleTimeout, metrics, logger, onConnectionStateChanged, instrumentation)
    {
    }

    public NetworkStreamNmeaReceiver(INmeaStreamReader reader, string host, int port, TimeProvider timeProvider, TimeSpan? retryPeriodicity = null, int retryAttemptLimit = 100, TimeSpan? idleTimeout = null, ApplicationMetrics? metrics = null, ILogger<NetworkStreamNmeaReceiver>? logger = null, Action<bool>? onConnectionStateChanged = null, ApplicationInstrumentation? instrumentation = null)
        : this(new StreamReaderConnection(reader ?? throw new ArgumentNullException(nameof(reader)), host, port), timeProvider, retryPeriodicity, retryAttemptLimit, idleTimeout, metrics, logger, onConnectionStateChanged, instrumentation)
    {
    }

    private NetworkStreamNmeaReceiver(INmeaConnection connection, TimeProvider timeProvider, TimeSpan? retryPeriodicity, int retryAttemptLimit, TimeSpan? idleTimeout, ApplicationMetrics? metrics, ILogger<NetworkStreamNmeaReceiver>? logger, Action<bool>? onConnectionStateChanged, ApplicationInstrumentation? instrumentation)
    {
        this.Host = connection.EndpointAddress;
        this.Port = connection.EndpointPort;

        // The connection-state callback reports the live TCP connection state (true on connect,
        // false on any disconnect/idle/error), so health reflects the real socket rather than the
        // host's start/stop lifetime.
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

    public string Host { get; }

    public int Port { get; }

    public int RetryAttemptLimit => this.core.Options.RetryAttemptLimit;

    public TimeSpan RetryPeriodicity => this.core.Options.RetryPeriodicity;

    public TimeSpan? IdleTimeout => this.core.Options.IdleTimeout;

    /// <summary>
    /// Streams NMEA lines from the network, reconnecting with linear backoff on failure or idle
    /// timeout. This is a native pull-based sequence: a yielded buffer is only guaranteed valid
    /// until the next <c>MoveNextAsync</c> (the underlying reader reuses its read buffer), which is
    /// safe here because each line is consumed synchronously before the next is requested.
    /// </summary>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default) =>
        this.core.GetAsync(cancellationToken);

    /// <summary>
    /// Backwards-compatible observable projection over <see cref="GetAsync"/>. Each line is copied
    /// so that observers - whose lifetimes this method does not control - receive an owned buffer.
    /// </summary>
    public IObservable<ReadOnlyMemory<byte>> GetObservable(CancellationToken cancellationToken = default) =>
        this.core.GetObservable(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await this.core.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Adapts an <see cref="INmeaStreamReader"/> plus a captured host and port to the
    /// transport-agnostic <see cref="INmeaConnection"/> consumed by the reconnect loop.
    /// </summary>
    private sealed class StreamReaderConnection(INmeaStreamReader reader, string host, int port) : INmeaConnection
    {
        public string EndpointAddress => host;

        public int EndpointPort => port;

        public string Transport => "tcp";

        public string? ProtocolName => null;

        public NmeaSourceKind Kind => NmeaSourceKind.Live;

        public bool Connected => reader.Connected;

        public Task ConnectAsync(CancellationToken cancellationToken) => reader.ConnectAsync(host, port, cancellationToken);

        public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken) => reader.ReadLineAsync(cancellationToken);

        public ValueTask DisposeAsync() => reader.DisposeAsync();
    }
}
