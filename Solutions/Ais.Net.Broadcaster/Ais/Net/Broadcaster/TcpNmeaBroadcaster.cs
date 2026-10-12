// <copyright file="TcpNmeaBroadcaster.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

using Ais.Net.Broadcaster.Telemetry;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ais.Net.Broadcaster;

/// <summary>
/// Broadcasts newline-terminated NMEA sentences to every client connected to a TCP listener: the
/// shape of the Norwegian Coastal Administration feed.
/// </summary>
/// <remarks>
/// Each client has its own bounded queue of framed lines, drained to its socket by a writer task,
/// so a slow client never blocks <see cref="BroadcastAsync"/> or the other clients. What happens
/// when a queue fills is governed by <see cref="BroadcasterOptions.SlowClient"/>. A reader task per
/// client discards anything the client sends and notices promptly when it disconnects.
/// </remarks>
public sealed class TcpNmeaBroadcaster : INmeaBroadcaster
{
    // Lines waiting in a client's queue are coalesced into writes of up to this many bytes, so a
    // burst does not become one send per sentence.
    private const int WriteBufferSize = 16 * 1024;

    private readonly ILogger logger;
    private readonly byte[] lineEnding;
    private readonly ConcurrentDictionary<ClientSession, byte> clients = new();
    private readonly CancellationTokenSource stopCts = new();
    private readonly Lock sync = new();
    private TcpListener? listener;
    private Task? acceptLoop;
    private Task? stopTask;
    private IPEndPoint? localEndPoint;
    private bool started;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpNmeaBroadcaster"/> class.
    /// </summary>
    /// <param name="options">The listener, framing and backpressure settings.</param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The port is outside 0 to 65535 or the client queue capacity is less than 1.
    /// </exception>
    public TcpNmeaBroadcaster(TcpBroadcasterOptions options, ILogger<TcpNmeaBroadcaster>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Address);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ClientQueueCapacity, 1);

        this.Options = options;
        this.logger = (ILogger?)logger ?? NullLogger.Instance;
        this.lineEnding = options.LineEnding == NmeaLineEnding.CrLf ? "\r\n"u8.ToArray() : "\n"u8.ToArray();
    }

    /// <summary>
    /// Gets the settings this broadcaster was created with.
    /// </summary>
    public TcpBroadcasterOptions Options { get; }

    /// <summary>
    /// Gets the endpoint the listener is bound to. When <see cref="BroadcasterOptions.Port"/> is 0
    /// this carries the port the operating system chose.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="StartAsync"/> has not completed.</exception>
    public IPEndPoint LocalEndPoint => this.localEndPoint ?? throw new InvalidOperationException("The broadcaster has not been started.");

    /// <inheritdoc/>
    /// <remarks>
    /// <c>tcp://address:port</c>, using the bound port once started.
    /// </remarks>
    public string Name => $"tcp://{this.localEndPoint ?? new IPEndPoint(this.Options.Address, this.Options.Port)}";

    /// <inheritdoc/>
    public int ClientCount => this.clients.Count;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The broadcaster has already been started or stopped.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (this.sync)
        {
            if (this.started || this.stopTask is not null)
            {
                throw new InvalidOperationException("A broadcaster can only be started once.");
            }

            TcpListener newListener = new(this.Options.Address, this.Options.Port);
            newListener.Start();

            this.listener = newListener;
            this.localEndPoint = (IPEndPoint)newListener.LocalEndpoint;
            this.started = true;
            CancellationToken stopToken = this.stopCts.Token;
            this.acceptLoop = Task.Run(() => this.AcceptLoopAsync(newListener, stopToken), CancellationToken.None);
        }

        this.logger.TcpListening(this.Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask BroadcastAsync(ReadOnlyMemory<byte> sentence, CancellationToken cancellationToken)
    {
        if (this.clients.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        byte[] line = new byte[sentence.Length + this.lineEnding.Length];
        sentence.Span.CopyTo(line);
        this.lineEnding.CopyTo(line.AsSpan(sentence.Length));

        foreach (ClientSession client in this.clients.Keys)
        {
            if (!client.Queue.Writer.TryWrite(line) && !client.IsClosed)
            {
                // Only a Disconnect policy queue can refuse a write: DropOldest always makes room.
                if (this.clients.TryRemove(client, out _))
                {
                    this.logger.TcpClientTooSlow(client.RemoteEndPoint, this.Options.ClientQueueCapacity);
                }

                client.Close();
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

        this.logger.TcpDisconnectingAll(sessions.Count);

        foreach (ClientSession session in sessions)
        {
            session.Close();
        }

        await Task.WhenAll(sessions.Select(s => s.Completion)).WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task ReadLoopAsync(ClientSession session)
    {
        // Inbound bytes are ignored; reading exists only to see the client go away.
        byte[] buffer = new byte[256];
        try
        {
            while (await session.Stream.ReadAsync(buffer, session.CancellationToken).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client went away or is being closed.
        }
    }

    private async Task StopCoreAsync()
    {
        if (!this.started)
        {
            this.stopCts.Dispose();
            return;
        }

        await this.stopCts.CancelAsync().ConfigureAwait(false);
        this.listener?.Stop();

        if (this.acceptLoop is not null)
        {
            await this.acceptLoop.ConfigureAwait(false);
        }

        await this.DisconnectAllAsync(CancellationToken.None).ConfigureAwait(false);
        this.listener?.Dispose();
        this.stopCts.Dispose();

        this.logger.TcpStopped(this.Name);
    }

    private async Task AcceptLoopAsync(TcpListener activeListener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await activeListener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested || ex is ObjectDisposedException)
            {
                // The listener has been stopped.
                return;
            }
            catch (SocketException ex)
            {
                // A connection that failed between SYN and accept; the listener itself is fine.
                this.logger.TcpAcceptError(ex, this.Name);
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                return;
            }

            this.AddClient(socket);
        }
    }

    private void AddClient(Socket socket)
    {
        socket.NoDelay = true;

        Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(this.Options.ClientQueueCapacity)
        {
            FullMode = this.Options.SlowClient == SlowClientPolicy.DropOldest ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        ClientSession session = new(socket, queue);
        this.clients.TryAdd(session, 0);
        this.logger.TcpClientConnected(session.RemoteEndPoint, this.clients.Count);

        _ = Task.Run(() => this.RunClientAsync(session), CancellationToken.None);
    }

    private async Task RunClientAsync(ClientSession session)
    {
        try
        {
            Task writer = this.WriteLoopAsync(session);
            Task reader = ReadLoopAsync(session);

            await Task.WhenAny(writer, reader).ConfigureAwait(false);
            session.Close();
            await Task.WhenAll(writer, reader).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Both loops handle their own failures; nothing else can usefully be done here.
        }
        finally
        {
            session.Close();
            this.clients.TryRemove(session, out _);
            this.logger.TcpClientDisconnected(session.RemoteEndPoint, this.clients.Count);
            session.MarkCompleted();
        }
    }

    private async Task WriteLoopAsync(ClientSession session)
    {
        ChannelReader<byte[]> reader = session.Queue.Reader;
        CancellationToken cancellationToken = session.CancellationToken;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(WriteBufferSize);
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                int buffered = 0;
                while (reader.TryRead(out byte[]? line))
                {
                    if (buffered > 0 && buffered + line.Length > buffer.Length)
                    {
                        await session.Stream.WriteAsync(buffer.AsMemory(0, buffered), cancellationToken).ConfigureAwait(false);
                        buffered = 0;
                    }

                    if (line.Length > buffer.Length)
                    {
                        await session.Stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        line.CopyTo(buffer, buffered);
                        buffered += line.Length;
                    }
                }

                if (buffered > 0)
                {
                    await session.Stream.WriteAsync(buffer.AsMemory(0, buffered), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            if (!session.IsClosed && ex is IOException or SocketException)
            {
                this.logger.TcpClientWriteError(ex, session.RemoteEndPoint);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// One connected client: its socket, its queue of framed lines and the lifetime of its loops.
    /// </summary>
    private sealed class ClientSession
    {
        private readonly Socket socket;
        private readonly CancellationTokenSource cts = new();
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int closed;

        public ClientSession(Socket socket, Channel<byte[]> queue)
        {
            this.socket = socket;
            this.Queue = queue;
            this.Stream = new NetworkStream(socket, ownsSocket: true);
            this.CancellationToken = this.cts.Token;
            this.RemoteEndPoint = socket.RemoteEndPoint?.ToString() ?? "unknown";
        }

        public Channel<byte[]> Queue { get; }

        public NetworkStream Stream { get; }

        public CancellationToken CancellationToken { get; }

        public string RemoteEndPoint { get; }

        public bool IsClosed => Volatile.Read(ref this.closed) != 0;

        public Task Completion => this.completion.Task;

        public void Close()
        {
            if (Interlocked.Exchange(ref this.closed, 1) != 0)
            {
                return;
            }

            this.Queue.Writer.TryComplete();
            this.cts.Cancel();

            try
            {
                this.socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // Already gone.
            }

            this.Stream.Dispose();
        }

        public void MarkCompleted()
        {
            // Close has run by now, so nothing touches the token source again.
            this.cts.Dispose();
            this.completion.TrySetResult();
        }
    }
}
