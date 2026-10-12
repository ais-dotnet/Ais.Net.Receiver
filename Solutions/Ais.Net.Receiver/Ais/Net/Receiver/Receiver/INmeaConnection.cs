// <copyright file="INmeaConnection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// A transport-agnostic, already-addressed connection to an NMEA feed. The endpoint is captured
/// when the implementation is constructed, so connecting requires only a cancellation token.
/// </summary>
public interface INmeaConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets a human-readable endpoint address for logs and spans, e.g. <c>153.44.253.27</c> or
    /// <c>wss://feed.example/ais</c>.
    /// </summary>
    string EndpointAddress { get; }

    /// <summary>
    /// Gets the endpoint port for logs and spans.
    /// </summary>
    int EndpointPort { get; }

    /// <summary>
    /// Gets the OpenTelemetry <c>network.transport</c> value for this connection, e.g. <c>tcp</c>.
    /// </summary>
    string Transport { get; }

    /// <summary>
    /// Gets the optional OpenTelemetry <c>network.protocol.name</c> value for this connection, e.g.
    /// <c>websocket</c>, or <see langword="null"/> when not applicable.
    /// </summary>
    string? ProtocolName { get; }

    /// <summary>
    /// Gets whether this connection is a live feed, which is reconnected with backoff when it ends
    /// or fails, or a finite source, which completes the receiver at end of stream.
    /// </summary>
    NmeaSourceKind Kind { get; }

    /// <summary>
    /// Gets whether the connection is established.
    /// </summary>
    bool Connected { get; }

    /// <summary>
    /// Establishes the connection to the endpoint captured at construction.
    /// </summary>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>A task that completes when the connection is established.</returns>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads a line asynchronously, returning <see langword="null"/> at end of stream. The returned
    /// memory is only guaranteed to remain valid until the next call to <see cref="ReadLineAsync"/>
    /// or <see cref="IAsyncDisposable.DisposeAsync"/> on this connection, as implementations may
    /// reuse a single read buffer; callers that need the data beyond that must copy it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The line read, or <see langword="null"/> at end of stream.</returns>
    ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken);
}
