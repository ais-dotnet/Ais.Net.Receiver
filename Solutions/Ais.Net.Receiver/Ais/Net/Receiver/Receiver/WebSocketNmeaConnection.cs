// <copyright file="WebSocketNmeaConnection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net.WebSockets;

using Ais.Net.Receiver.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// An <see cref="INmeaConnection"/> that reads NMEA sentences from a WebSocket (<c>ws://</c> or
/// <c>wss://</c>) feed using <see cref="ClientWebSocket"/>.
/// </summary>
/// <remarks>
/// <para>
/// Feeds differ in how they frame sentences, so lines are delimited as follows: every <c>\n</c>
/// within a message terminates a line; the end of a message also terminates a line (so a feed that
/// sends one sentence per message with no newline works); no empty line is produced for the
/// remainder after a trailing <c>\n</c> at the end of a message; a line may span several frames of
/// one message; and both text and binary messages are accepted. A trailing <c>\r</c> is trimmed.
/// </para>
/// <para>
/// Each message is read through <see cref="WebSocketStream.CreateReadableMessageStream(WebSocket)"/>,
/// which ends at the message boundary, and framed by <see cref="NmeaLineReader"/>. A line (or
/// unterminated message) longer than <see cref="NmeaLineReader.DefaultMaxLineLength"/> bytes is
/// therefore discarded, and reading resyncs on the next newline or message boundary.
/// </para>
/// </remarks>
public sealed class WebSocketNmeaConnection : INmeaConnection
{
    // Bounds how long a connect attempt (TCP, TLS and the HTTP upgrade) may hang before we give up
    // and let the caller's retry loop schedule another.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    // Bounds how long DisposeAsync waits to send the close frame to a peer that may already be gone.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private readonly Uri url;
    private readonly string urlText;
    private readonly ILogger logger;
    private readonly Action<ClientWebSocketOptions>? configureOptions;

    private ClientWebSocket? socket;

    // The current WebSocket message, read as a stream that ends at the message boundary, and the
    // line reader framing it. Both are replaced for each message.
    private Stream? messageStream;
    private NmeaLineReader? messageReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketNmeaConnection"/> class.
    /// </summary>
    /// <param name="url">The absolute <c>ws://</c> or <c>wss://</c> URL of the feed.</param>
    /// <param name="logger">An optional logger.</param>
    /// <param name="configureOptions">
    /// An optional callback that configures each new <see cref="ClientWebSocket"/> before it connects,
    /// for example to add request headers or sub-protocols. It runs after the defaults are applied.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="url"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute <c>ws</c> or <c>wss</c> URI.</exception>
    public WebSocketNmeaConnection(Uri url, ILogger<WebSocketNmeaConnection>? logger = null, Action<ClientWebSocketOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeWs && url.Scheme != Uri.UriSchemeWss))
        {
            throw new ArgumentException("The URL must be an absolute ws:// or wss:// URI.", nameof(url));
        }

        this.url = url;
        this.urlText = url.ToString();
        this.logger = logger ?? NullLogger<WebSocketNmeaConnection>.Instance;
        this.configureOptions = configureOptions;
    }

    /// <inheritdoc/>
    public string EndpointAddress => this.url.Host;

    /// <inheritdoc/>
    public int EndpointPort => this.url.Port;

    /// <inheritdoc/>
    public string Transport => "tcp";

    /// <inheritdoc/>
    public string? ProtocolName => "websocket";

    /// <inheritdoc/>
    public NmeaSourceKind Kind => NmeaSourceKind.Live;

    /// <inheritdoc/>
    public bool Connected => this.socket?.State == WebSocketState.Open;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await this.DisposeAsync().ConfigureAwait(false);

        ClientWebSocket newSocket = new();
        newSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        this.configureOptions?.Invoke(newSocket.Options);
        this.socket = newSocket;

        this.logger.WebSocketConnecting(this.urlText);

        try
        {
            using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);

            await newSocket.ConnectAsync(this.url, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled - a normal shutdown, not a failure. ClientWebSocket may surface
            // this as a WebSocketException rather than an OperationCanceledException, so normalise it.
            this.logger.WebSocketConnectCancelled(this.urlText);
            await this.DisposeAsync().ConfigureAwait(false);

            if (ex is OperationCanceledException)
            {
                throw;
            }

            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The connect timeout elapsed rather than the caller cancelling. Surface it as a
            // timeout so the retry loop treats it like any other connection failure.
            this.logger.WebSocketConnectionTimedOut(this.urlText);
            await this.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException($"Timed out connecting to {this.urlText}.");
        }
        catch (Exception ex)
        {
            this.logger.WebSocketConnectionFailed(ex, this.urlText);
            await this.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        this.logger.WebSocketConnected(this.urlText, newSocket.SubProtocol);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The returned memory is a slice of the buffer owned by the line reader for the current
    /// WebSocket message. It is only valid until the next call to <see cref="ReadLineAsync"/> or
    /// <see cref="DisposeAsync"/>; callers that need the data beyond that must copy it.
    /// </remarks>
    public async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (this.socket is not { } current || !this.Connected)
        {
            return null;
        }

        try
        {
            while (true)
            {
                if (this.messageReader is null)
                {
                    if (current.State != WebSocketState.Open)
                    {
                        // A Close frame arrived (the message stream reports it as end of stream with
                        // no data, leaving the socket in CloseReceived), or the socket faulted: report
                        // end of stream so the receiver reconnects.
                        this.logger.WebSocketClosedByPeer(
                            this.urlText,
                            current.CloseStatus?.ToString() ?? "None",
                            current.CloseStatusDescription);
                        return null;
                    }

                    // Each WebSocket message is read as its own stream, which ends at the message
                    // boundary. The line reader splits on '\n' within the message and emits any
                    // unterminated remainder as the final line, so the message boundary also
                    // terminates a line, with no empty line after a trailing '\n'.
                    this.messageStream = WebSocketStream.CreateReadableMessageStream(current);
                    this.messageReader = NmeaLineReader.Create(this.messageStream, this.logger);
                }

                ReadOnlyMemory<byte>? line = await this.messageReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is not null)
                {
                    return line;
                }

                // End of this message: move on to the next one.
                await this.DisposeMessageReaderAsync().ConfigureAwait(false);
            }
        }
        catch (WebSocketException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A reset or protocol failure after connect: report end of stream so the receiver
            // reconnects, mirroring how the TCP reader treats a connection reset.
            this.logger.WebSocketReceiveError(ex, this.urlText);
            await this.DisposeMessageReaderAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.DisposeMessageReaderAsync().ConfigureAwait(false);

        ClientWebSocket? current = this.socket;
        this.socket = null;

        if (current is not null)
        {
            if (current.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                this.logger.WebSocketGracefulShutdown(this.urlText);

                try
                {
                    using CancellationTokenSource closeCts = new(CloseTimeout);
                    await current.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "shutdown", closeCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore close errors - the peer may already be gone.
                }

                this.logger.WebSocketDisconnected(this.urlText);
            }

            try { current.Dispose(); } catch { /* Ignore any errors during cleanup */ }
        }
    }

    private async ValueTask DisposeMessageReaderAsync()
    {
        NmeaLineReader? reader = this.messageReader;
        Stream? stream = this.messageStream;
        this.messageReader = null;
        this.messageStream = null;

        if (reader is not null)
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }

        if (stream is not null)
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore any errors during cleanup.
            }
        }
    }
}
