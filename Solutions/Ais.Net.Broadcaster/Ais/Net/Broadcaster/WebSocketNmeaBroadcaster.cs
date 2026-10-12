// <copyright file="WebSocketNmeaBroadcaster.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;

using Ais.Net.Broadcaster.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Broadcaster;

/// <summary>
/// Broadcasts NMEA sentences as WebSocket text messages from a minimal Kestrel endpoint.
/// </summary>
/// <remarks>
/// <para>
/// A WebSocket upgrade request for <see cref="WebSocketBroadcasterOptions.Path"/> is accepted;
/// any other request for that path is answered with 400, and any other path with 404.
/// </para>
/// <para>
/// Each client has its own bounded queue, drained by a sender task that frames sentences per
/// <see cref="WebSocketBroadcasterOptions.Framing"/>, so a slow client never blocks
/// <see cref="BroadcastAsync"/> or the other clients. What happens when a queue fills is
/// governed by <see cref="BroadcasterOptions.SlowClient"/>. A receive loop per client discards
/// inbound messages and completes the close handshake when the client closes.
/// </para>
/// </remarks>
public sealed class WebSocketNmeaBroadcaster : INmeaBroadcaster
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private readonly ILoggerFactory? loggerFactory;
    private readonly ILogger logger;
    private readonly byte[] lineEnding;
    private readonly PathString path;
    private readonly ConcurrentDictionary<ClientSession, byte> clients = new();
    private readonly Lock sync = new();
    private WebApplication? app;
    private Task? stopTask;
    private Uri? url;
    private bool started;
    private volatile bool stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSocketNmeaBroadcaster"/> class.
    /// </summary>
    /// <param name="options">The listener, framing and backpressure settings.</param>
    /// <param name="loggerFactory">
    /// An optional logger factory. It is handed to the embedded Kestrel host, so the server's own
    /// logs route with the application's; with none, the host logs nothing.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The port is outside 0 to 65535, the client queue capacity or batch size is less than 1, or
    /// the batch interval is not positive.
    /// </exception>
    public WebSocketNmeaBroadcaster(WebSocketBroadcasterOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Address);
        ArgumentNullException.ThrowIfNull(options.Path);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ClientQueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.BatchInterval, TimeSpan.Zero);

        this.Options = options;
        this.loggerFactory = loggerFactory;
        this.logger = (ILogger?)loggerFactory?.CreateLogger<WebSocketNmeaBroadcaster>() ?? NullLogger.Instance;
        this.lineEnding = options.LineEnding == NmeaLineEnding.CrLf ? "\r\n"u8.ToArray() : "\n"u8.ToArray();
        this.path = new PathString(options.Path.StartsWith('/') ? options.Path : "/" + options.Path);
    }

    /// <summary>
    /// Gets the settings this broadcaster was created with.
    /// </summary>
    public WebSocketBroadcasterOptions Options { get; }

    /// <summary>
    /// Gets the <c>ws://address:port/path</c> address clients connect to. When
    /// <see cref="BroadcasterOptions.Port"/> is 0 this carries the port the operating system chose.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="StartAsync"/> has not completed.</exception>
    public Uri Url => this.url ?? throw new InvalidOperationException("The broadcaster has not been started.");

    /// <inheritdoc/>
    /// <remarks>
    /// The <see cref="Url"/> once started; before that, the configured address, port and path.
    /// </remarks>
    public string Name => (this.url ?? this.BuildUrl(this.Options.Port)).ToString();

    /// <inheritdoc/>
    public int ClientCount => this.clients.Count;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The broadcaster has already been started or stopped.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        WebApplication newApp;
        lock (this.sync)
        {
            if (this.started || this.stopTask is not null)
            {
                throw new InvalidOperationException("A broadcaster can only be started once.");
            }

            this.started = true;
            newApp = this.BuildApp();
            this.app = newApp;
        }

        await newApp.StartAsync(cancellationToken).ConfigureAwait(false);

        string address = newApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        this.url = this.BuildUrl(new Uri(address).Port);

        this.logger.WebSocketListening(this.Name);
    }

    /// <inheritdoc/>
    public ValueTask BroadcastAsync(ReadOnlyMemory<byte> sentence, CancellationToken cancellationToken)
    {
        if (this.clients.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        // Frame once for every client: a whole message, or a terminated line to batch.
        byte[] item;
        if (this.Options.Framing == WebSocketFraming.SentencePerMessage)
        {
            item = sentence.ToArray();
        }
        else
        {
            item = new byte[sentence.Length + this.lineEnding.Length];
            sentence.Span.CopyTo(item);
            this.lineEnding.CopyTo(item.AsSpan(sentence.Length));
        }

        foreach (ClientSession client in this.clients.Keys)
        {
            if (!client.Queue.Writer.TryWrite(item) && !client.IsClosing)
            {
                // Only a Disconnect policy queue can refuse a write: DropOldest always makes room.
                // The client's socket is backed up, so a close frame could not get through: abort.
                if (this.clients.TryRemove(client, out _))
                {
                    this.logger.WebSocketClientTooSlow(client.RemoteEndPoint, this.Options.ClientQueueCapacity);
                }

                client.Abort();
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DisconnectAllAsync(CancellationToken cancellationToken)
    {
        List<ClientSession> sessions = [];
        foreach (ClientSession client in this.clients.Keys)
        {
            if (this.clients.TryRemove(client, out _))
            {
                sessions.Add(client);
            }
        }

        if (sessions.Count == 0)
        {
            return;
        }

        this.logger.WebSocketDisconnectingAll(sessions.Count);

        await Task.WhenAll(sessions.Select(s => s.CloseAsync(CloseTimeout))).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (this.sync)
        {
            this.stopTask ??= this.StopCoreAsync();
            return this.stopTask.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        this.stopping = true;

        if (this.app is not { } runningApp)
        {
            return;
        }

        await this.DisconnectAllAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await runningApp.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await runningApp.DisposeAsync().ConfigureAwait(false);
        }

        this.logger.WebSocketStopped(this.Name);
    }

    private Uri BuildUrl(int port)
    {
        // UriBuilder brackets IPv6 hosts; IPAddress.Any renders as 0.0.0.0.
        UriBuilder builder = new("ws", this.Options.Address.ToString(), port, this.path.Value);
        return builder.Uri;
    }

    private WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        // This is an embedded endpoint: the hosting application's configuration (for example a
        // Kestrel endpoints section) must not reshape it.
        builder.Configuration.Sources.Clear();

        builder.Logging.ClearProviders();
        if (this.loggerFactory is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton(this.loggerFactory));
        }

        // The embedded host must not take over the process's Ctrl+C and SIGTERM handling.
        builder.Services.AddSingleton<IHostLifetime, EmbeddedHostLifetime>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = CloseTimeout);
        builder.WebHost.ConfigureKestrel(k => k.Listen(this.Options.Address, this.Options.Port));

        WebApplication newApp = builder.Build();
        newApp.UseWebSockets();
        newApp.Run(this.HandleRequestAsync);
        return newApp;
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals(this.path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string remoteEndPoint = context.Connection.RemoteIpAddress is { } remoteAddress
            ? new IPEndPoint(remoteAddress, context.Connection.RemotePort).ToString()
            : "unknown";

        if (!context.WebSockets.IsWebSocketRequest)
        {
            this.logger.WebSocketRequestRejected(context.Request.Path.Value ?? string.Empty, remoteEndPoint);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (this.stopping)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(this.Options.ClientQueueCapacity)
        {
            FullMode = this.Options.SlowClient == SlowClientPolicy.DropOldest ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        ClientSession session = new(socket, queue, remoteEndPoint);
        this.clients.TryAdd(session, 0);
        this.logger.WebSocketClientConnected(remoteEndPoint, this.clients.Count);

        try
        {
            Task sender = this.SendLoopAsync(session);
            Task receiver = ReceiveLoopAsync(session);

            await Task.WhenAny(sender, receiver).ConfigureAwait(false);
            if (sender.IsCompleted && !receiver.IsCompleted && !session.IsClosing)
            {
                // The sender failed rather than being stopped for a close; tear the connection
                // down so the receive loop ends.
                session.Abort();
            }

            if (receiver.IsCompleted && !sender.IsCompleted)
            {
                session.StopSending();
            }

            await Task.WhenAll(sender, receiver).ConfigureAwait(false);
        }
        finally
        {
            this.clients.TryRemove(session, out _);
            this.logger.WebSocketClientDisconnected(remoteEndPoint, this.clients.Count);
            session.MarkCompleted();
        }
    }

    private static async Task ReceiveLoopAsync(ClientSession session)
    {
        // Inbound messages are discarded; receiving exists to see close frames and dropped clients.
        WebSocket socket = session.Socket;
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    session.StopSending();
                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        // The client started the close: complete the handshake.
                        using CancellationTokenSource closeCts = new(CloseTimeout);
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeCts.Token).ConfigureAwait(false);
                    }

                    return;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            // The client went away, or the connection was aborted.
        }
    }

    private async Task SendLoopAsync(ClientSession session)
    {
        try
        {
            if (this.Options.Framing == WebSocketFraming.SentencePerMessage)
            {
                await SendPerMessageAsync(session).ConfigureAwait(false);
            }
            else
            {
                await this.SendBatchesAsync(session).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            if (!session.IsClosing && ex is WebSocketException or IOException)
            {
                this.logger.WebSocketClientWriteError(ex, session.RemoteEndPoint);
            }
        }
    }

    private static async Task SendPerMessageAsync(ClientSession session)
    {
        ChannelReader<byte[]> reader = session.Queue.Reader;
        CancellationToken cancellationToken = session.CancellationToken;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (reader.TryRead(out byte[]? sentence))
            {
                await session.Socket.SendAsync(sentence, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendBatchesAsync(ClientSession session)
    {
        ChannelReader<byte[]> reader = session.Queue.Reader;
        CancellationToken cancellationToken = session.CancellationToken;
        int batchSize = this.Options.BatchSize;
        TimeSpan batchInterval = this.Options.BatchInterval;
        ArrayBufferWriter<byte> batch = new(4096);

        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            batch.ResetWrittenCount();
            int count = 0;
            long batchStarted = Stopwatch.GetTimestamp();

            while (true)
            {
                while (count < batchSize && reader.TryRead(out byte[]? line))
                {
                    batch.Write(line);
                    count++;
                }

                if (count >= batchSize)
                {
                    break;
                }

                TimeSpan remaining = batchInterval - Stopwatch.GetElapsedTime(batchStarted);
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using CancellationTokenSource waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                waitCts.CancelAfter(remaining);
                try
                {
                    if (!await reader.WaitToReadAsync(waitCts.Token).ConfigureAwait(false))
                    {
                        // The queue was completed: the client is closing, so the partial batch goes nowhere.
                        return;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The batch interval elapsed.
                    break;
                }
            }

            if (count > 0)
            {
                await session.Socket.SendAsync(batch.WrittenMemory, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One connected client: its socket, its queue and the signals that end its loops.
    /// </summary>
    private sealed class ClientSession
    {
        private readonly CancellationTokenSource cts = new();
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int closing;
        private int completed;

        public ClientSession(WebSocket socket, Channel<byte[]> queue, string remoteEndPoint)
        {
            this.Socket = socket;
            this.Queue = queue;
            this.RemoteEndPoint = remoteEndPoint;
            this.CancellationToken = this.cts.Token;
        }

        public WebSocket Socket { get; }

        public Channel<byte[]> Queue { get; }

        public string RemoteEndPoint { get; }

        public CancellationToken CancellationToken { get; }

        public bool IsClosing => Volatile.Read(ref this.closing) != 0;

        /// <summary>
        /// Stops the sender gracefully: no more sentences are accepted, and the sender exits once
        /// its current send completes.
        /// </summary>
        public void StopSending()
        {
            Interlocked.Exchange(ref this.closing, 1);
            this.Queue.Writer.TryComplete();
        }

        /// <summary>
        /// Sends a normal close frame and waits, up to <paramref name="timeout"/>, for the client
        /// to acknowledge it and the session to end; aborts the connection if it does not.
        /// </summary>
        public async Task CloseAsync(TimeSpan timeout)
        {
            this.StopSending();

            try
            {
                using CancellationTokenSource closeCts = new(timeout);
                if (this.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await this.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "disconnect", closeCts.Token).ConfigureAwait(false);
                }

                await this.completion.Task.WaitAsync(closeCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException)
            {
                this.Abort();
                await this.completion.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tears the connection down at once, failing any send or receive in progress.
        /// </summary>
        public void Abort()
        {
            this.StopSending();
            if (Volatile.Read(ref this.completed) == 0)
            {
                try
                {
                    this.cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The session ended concurrently.
                }
            }

            this.Socket.Abort();
        }

        public void MarkCompleted()
        {
            Interlocked.Exchange(ref this.completed, 1);
            this.completion.TrySetResult();
        }
    }

    /// <summary>
    /// A host lifetime that does nothing, so the embedded Kestrel host neither listens for Ctrl+C
    /// or SIGTERM nor delays start-up; the owning application controls its lifetime.
    /// </summary>
    private sealed class EmbeddedHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
