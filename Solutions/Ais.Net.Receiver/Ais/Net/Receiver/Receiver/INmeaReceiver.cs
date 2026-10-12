// <copyright file="INmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// A source of NMEA sentences, one line at a time.
/// </summary>
public interface INmeaReceiver : IAsyncDisposable
{
    /// <summary>
    /// Streams NMEA lines, each without its line terminator.
    /// </summary>
    /// <param name="cancellationToken">Stops the stream.</param>
    /// <returns>The sequence of NMEA lines, as the original bytes.</returns>
    /// <remarks>
    /// A yielded buffer is only guaranteed to remain valid until the next call to
    /// <c>MoveNextAsync</c> on the enumerator, as implementations may reuse a single read buffer.
    /// Consumers that need a line beyond that - for example to collect lines into a list - must
    /// copy it (e.g. with <see cref="ReadOnlyMemory{T}.ToArray"/>).
    /// </remarks>
    IAsyncEnumerable<ReadOnlyMemory<byte>> GetAsync(CancellationToken cancellationToken = default);
}
