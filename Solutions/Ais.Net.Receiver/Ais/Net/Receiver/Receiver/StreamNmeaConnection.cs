// <copyright file="StreamNmeaConnection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// An <see cref="INmeaConnection"/> over anything that can open a <see cref="Stream"/> of
/// newline-delimited NMEA sentences - a local file, a blob, a socket. Owns the stream and the
/// <see cref="NmeaLineReader"/> that frames it; a derived class supplies only the stream.
/// </summary>
/// <remarks>
/// Bytes pass through untouched: lines are split on <c>\n</c> (with one trailing <c>\r</c>
/// trimmed) without any text decoding, so a byte above 0x7F - e.g. in a tag block - round-trips
/// exactly.
/// </remarks>
public abstract class StreamNmeaConnection : INmeaConnection
{
    private readonly TimeSpan lineDelay;
    private readonly ILogger? logger;
    private Stream? stream;
    private NmeaLineReader? lineReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamNmeaConnection"/> class.
    /// </summary>
    /// <param name="endpointAddress">A human-readable address for the source, for logs and spans.</param>
    /// <param name="lineDelay">
    /// An optional delay before each line is read, e.g. to replay a recording at a simulated live
    /// pace. <see langword="null"/> or <see cref="TimeSpan.Zero"/> reads as fast as the stream allows.
    /// </param>
    /// <param name="logger">An optional logger, used to report discarded over-long lines.</param>
    protected StreamNmeaConnection(string endpointAddress, TimeSpan? lineDelay = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endpointAddress);

        this.EndpointAddress = endpointAddress;
        this.lineDelay = lineDelay ?? TimeSpan.Zero;
        this.logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="NmeaSourceKind.Finite"/> by default: end of stream means the data is exhausted.
    /// </remarks>
    public virtual NmeaSourceKind Kind => NmeaSourceKind.Finite;

    /// <inheritdoc/>
    public string EndpointAddress { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Zero by default, as most stream sources have no port.
    /// </remarks>
    public virtual int EndpointPort => 0;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>file</c> by default.
    /// </remarks>
    public virtual string Transport => "file";

    /// <inheritdoc/>
    public virtual string? ProtocolName => null;

    /// <inheritdoc/>
    /// <remarks>
    /// <see langword="true"/> while a stream is open.
    /// </remarks>
    public virtual bool Connected => this.lineReader is not null;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await this.DisposeAsync().ConfigureAwait(false);

        this.stream = await this.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        this.lineReader = NmeaLineReader.Create(this.stream, this.logger);
    }

    /// <inheritdoc/>
    public virtual async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (this.lineReader is not { } reader)
        {
            return null;
        }

        // Replay pacing: the delay comes before each line, matching the original file receiver.
        if (this.lineDelay > TimeSpan.Zero)
        {
            await Task.Delay(this.lineDelay, cancellationToken).ConfigureAwait(false);
        }

        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the line reader and then the stream. Safe to call more than once, and called by
    /// <see cref="ConnectAsync"/> before each new stream is opened.
    /// </summary>
    /// <returns>A task that completes when the resources have been released.</returns>
    public virtual async ValueTask DisposeAsync()
    {
        if (this.lineReader is { } reader)
        {
            this.lineReader = null;
            await reader.DisposeAsync().ConfigureAwait(false);
        }

        if (this.stream is { } s)
        {
            this.stream = null;
            await s.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Opens the stream of NMEA sentences to read. Called on each <see cref="ConnectAsync"/>; this
    /// class owns the returned stream and disposes it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The stream.</returns>
    protected abstract ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken);
}
