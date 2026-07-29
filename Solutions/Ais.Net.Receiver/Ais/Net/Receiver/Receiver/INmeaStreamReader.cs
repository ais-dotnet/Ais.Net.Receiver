// <copyright file="INmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Abstracts network stream reading operations for NMEA messages
/// </summary>
public interface INmeaStreamReader : IAsyncDisposable
{
    /// <summary>
    /// Establishes a connection to the specified host and port
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken);
    
    /// <summary>
    /// Reads a line of text asynchronously. The returned memory is only guaranteed to remain valid
    /// until the next call to <see cref="ReadLineAsync"/> or <see cref="IAsyncDisposable.DisposeAsync"/>
    /// on this reader, as implementations may reuse a single read buffer; callers that need the data
    /// beyond that must copy it.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets whether the connection is established
    /// </summary>
    bool Connected { get; }
}