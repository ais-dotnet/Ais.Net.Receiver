// <copyright file="TcpNmeaConnection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net.Sockets;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// A <see cref="NmeaSourceKind.Live"/> connection that reads newline-delimited NMEA sentences from
/// a raw TCP feed. Line framing comes from <see cref="StreamNmeaConnection"/>; this class owns the
/// <see cref="TcpClient"/>, its socket options, connect timeout, failure classification and
/// graceful shutdown.
/// </summary>
public sealed class TcpNmeaConnection : StreamNmeaConnection
{
    // Bounds how long a connect attempt may hang before we give up and let the caller's retry
    // loop schedule another. A blocked SYN can otherwise stall well past any useful timeout.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly string host;
    private readonly int port;
    private readonly ILogger logger;
    private TcpClient? tcpClient;

    // Diagnostic: logs the shape of the first read after each connect, so a feed that accepts the
    // connection but never sends is distinguishable from one that was never reachable. Reset on
    // connect rather than per ReadLineAsync call, which would log on every line.
    private bool firstReadPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpNmeaConnection"/> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the feed.</param>
    /// <param name="port">The TCP port of the feed, 1 to 65535.</param>
    /// <param name="logger">
    /// An optional logger. It is deliberately the non-generic <see cref="ILogger"/> so that a
    /// wrapper such as <see cref="TcpClientNmeaStreamReader"/> can pass its own logger through and
    /// keep its log category.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="host"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is outside 1 to 65535.</exception>
    public TcpNmeaConnection(string host, int port, ILogger? logger = null)
        : base(ValidateHost(host), lineDelay: null, logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        this.host = host;
        this.port = port;
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="NmeaSourceKind.Live"/>: end of stream means the feed dropped, so reconnect.
    /// </remarks>
    public override NmeaSourceKind Kind => NmeaSourceKind.Live;

    /// <inheritdoc/>
    public override int EndpointPort => this.port;

    /// <inheritdoc/>
    /// <remarks>
    /// Always <c>tcp</c>.
    /// </remarks>
    public override string Transport => "tcp";

    /// <inheritdoc/>
    /// <remarks>
    /// Always <see langword="null"/>: a raw TCP feed has no application protocol name.
    /// </remarks>
    public override string? ProtocolName => null;

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> while the socket reports connected and a stream is open. Note that
    /// <see cref="TcpClient.Connected"/> does not update until an I/O operation fails, so a dropped
    /// feed is detected by <see cref="ReadLineAsync"/> returning <see langword="null"/>.
    /// </remarks>
    public override bool Connected => this.tcpClient?.Connected == true && base.Connected;

    /// <inheritdoc/>
    /// <remarks>
    /// Adds TCP diagnostics around the base read: the first read after each connect is announced
    /// before it is awaited and reported when it completes, and end of stream is logged.
    /// </remarks>
    public override async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (this.tcpClient is null)
        {
            return null;
        }

        // Announce the first read *before* awaiting it. A feed that completes the handshake and
        // then goes silent never returns from the read, so logging only on completion could not
        // report the very case this diagnostic exists for: the pairing of this event with no
        // subsequent TcpFirstRead is what identifies an accepted-but-silent connection.
        bool awaitingFirstRead = this.firstReadPending;
        if (awaitingFirstRead)
        {
            this.firstReadPending = false;
            this.logger.TcpAwaitingFirstRead(this.host, this.port);
        }

        ReadOnlyMemory<byte>? line = await base.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (awaitingFirstRead)
        {
            // Reports the first line (rather than the first raw buffer) and whether the stream had
            // already ended.
            this.logger.TcpFirstRead(this.host, this.port, line?.Length ?? 0, line is null);
        }

        if (line is null)
        {
            this.logger.TcpStreamEof(this.host, this.port);
        }

        return line;
    }

    /// <summary>
    /// Shuts the socket down gracefully (sending FIN), releases the line reader and stream, and
    /// disposes the <see cref="TcpClient"/>. Safe to call more than once, and called by
    /// <see cref="StreamNmeaConnection.ConnectAsync"/> before each new connection.
    /// </summary>
    /// <returns>A task that completes when the resources have been released.</returns>
    public override async ValueTask DisposeAsync()
    {
        bool wasConnected = this.tcpClient?.Connected == true;

        this.firstReadPending = false;

        // Send FIN before tearing the stream down, so the peer sees an orderly close rather than a
        // reset. Must happen while the socket is still open, hence before the stream is disposed.
        if (wasConnected && this.tcpClient?.Client is { } socket)
        {
            try
            {
                this.logger.TcpGracefulShutdown(this.host, this.port);
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // Ignore shutdown errors - the peer may already be gone.
            }
        }

        try
        {
            // Completes the line reader, then disposes the stream.
            await base.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore any errors during cleanup.
        }

        if (this.tcpClient is { } client)
        {
            this.tcpClient = null;
            try
            {
                client.Dispose();
            }
            catch
            {
                // Ignore any errors during cleanup.
            }
        }

        if (wasConnected)
        {
            this.logger.TcpDisconnected(this.host, this.port);
        }
    }

    /// <inheritdoc/>
    protected override async ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken)
    {
        // Deliberately no ReceiveTimeout/SendTimeout: those map to SO_RCVTIMEO/SO_SNDTIMEO, which only
        // affect the synchronous Socket.Receive/Send paths. Every read here goes through
        // PipeReader.ReadAsync, so setting them would advertise a stall bound that does not exist -
        // the actual bound is the receiver's idle timeout plus keepalive below. Likewise no
        // LingerState: this connection never sends payload data, so there is nothing to flush on
        // close, and SO_LINGER would make the synchronous Dispose() in DisposeAsync block until the
        // FIN is acknowledged (up to the linger timeout) on exactly the dead peers this class must
        // shed fast.
        TcpClient client = new()
        {
            ReceiveBufferSize = 65_536, // 64KB buffer for bursty traffic
            NoDelay = true,             // Disable Nagle's algorithm for lower latency
        };
        this.tcpClient = client;

        // Keepalive is what actually detects a half-open connection: an AIS feed that silently
        // disappears leaves the socket readable-but-idle, and without probes we would wait for the
        // idle timeout on every drop.
        Socket socket = client.Client;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);

        // Retry count is settable on Linux and macOS only; Windows fixes it at 10.
        if (!OperatingSystem.IsWindows())
        {
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }

        this.logger.TcpSocketConfigured(this.host, this.port);

        try
        {
            this.logger.TcpConnecting(this.host, this.port);

            using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);

            await client.ConnectAsync(this.host, this.port, connectCts.Token).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();

            this.firstReadPending = true;
            this.logger.TcpConnected(this.host, this.port);

            return stream;
        }
        catch (SocketException ex)
        {
            // Classify the common failures so operators can tell "feed is down" from "DNS is wrong"
            // from "we cannot route there" without reading stack traces.
            switch (ex.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    this.logger.TcpConnectionRefused(this.host, this.port);
                    break;
                case SocketError.HostNotFound:
                    this.logger.TcpHostNotFound(this.host, this.port);
                    break;
                case SocketError.TimedOut:
                    this.logger.TcpConnectionTimedOut(this.host, this.port);
                    break;
                case SocketError.NetworkUnreachable:
                    this.logger.TcpNetworkUnreachable(this.host, this.port);
                    break;
                case SocketError.ConnectionReset:
                    this.logger.TcpConnectionReset(this.host, this.port);
                    break;
                case SocketError.ConnectionAborted:
                    this.logger.TcpConnectionAborted(this.host, this.port);
                    break;
                default:
                    this.logger.TcpConnectionFailed(ex, this.host, this.port);
                    break;
            }

            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The connect timeout elapsed rather than the caller cancelling. Surface it as a
            // timeout so the retry loop treats it like any other connection failure.
            this.logger.TcpConnectionTimedOut(this.host, this.port);
            await this.DisposeAsync().ConfigureAwait(false);
            throw new SocketException((int)SocketError.TimedOut);
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled - a normal shutdown, not a failure. Logging this at Error (as the
            // generic handler below would) puts an ERROR record in the log on every clean SIGTERM or
            // Ctrl-C, which is exactly the kind of routine noise that trains operators to ignore the
            // severity.
            this.logger.TcpConnectCancelled(this.host, this.port);
            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            // If connection fails, clean up resources
            this.logger.TcpConnectionFailed(ex, this.host, this.port);
            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string ValidateHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return host;
    }
}
