// <copyright file="StreamNmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Base class for receivers that read NMEA sentences line by line from a <see cref="Stream"/> - a
/// local file, a blob, anything a derived class can open. Owns the line loop, the optional
/// inter-line delay, and disposal; a derived class supplies only the stream.
/// </summary>
/// <remarks>
/// This is a facade over an <see cref="NmeaReceiver"/> reading a <see cref="NmeaSourceKind.Finite"/>
/// <see cref="StreamNmeaConnection"/> whose stream comes from <see cref="OpenStreamAsync"/>. Bytes
/// pass through untouched: lines are split on <c>\n</c> (with one trailing <c>\r</c> trimmed)
/// without any text decoding, so a byte above 0x7F - e.g. in a tag block - round-trips exactly.
/// </remarks>
public abstract class StreamNmeaReceiver : INmeaReceiver
{
    private readonly NmeaReceiver receiver;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamNmeaReceiver"/> class that reads as fast
    /// as the stream allows.
    /// </summary>
    protected StreamNmeaReceiver()
        : this(TimeSpan.Zero)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamNmeaReceiver"/> class that pauses between
    /// lines, e.g. to simulate a live feed from a recording.
    /// </summary>
    /// <param name="delay">The delay before each line.</param>
    protected StreamNmeaReceiver(TimeSpan delay)
    {
        this.receiver = new NmeaReceiver(new OwnerStreamConnection(this, delay), TimeProvider.System);
    }

    /// <summary>
    /// Reads the stream's NMEA sentences, one line at a time.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The sentences, as the original bytes.</returns>
    /// <remarks>
    /// Unlike the live receivers, each yielded line is an owned copy rather than a slice of a reused
    /// read buffer. Callers of the file and blob receivers commonly collect the lines (e.g. with
    /// <c>ToListAsync</c>), which would otherwise leave every collected entry aliasing the same,
    /// since-overwritten buffer. The copy keeps that pattern safe; finite sources are not on a hot
    /// path where the allocation matters.
    /// </remarks>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ReadOnlyMemory<byte> line in this.receiver.GetAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return line.ToArray();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.receiver.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Opens the stream of NMEA sentences to read. Called once, at the start of enumeration; the
    /// base class owns the returned stream and disposes it.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stream.</returns>
    protected abstract ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adapts the receiver's <see cref="OpenStreamAsync"/> extension point to a
    /// <see cref="StreamNmeaConnection"/>.
    /// </summary>
    private sealed class OwnerStreamConnection(StreamNmeaReceiver owner, TimeSpan delay)
        : StreamNmeaConnection(owner.GetType().Name, delay)
    {
        protected override ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken) =>
            owner.OpenStreamAsync(cancellationToken);
    }
}
