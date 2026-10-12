// <copyright file="INmeaBroadcaster.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Broadcaster;

/// <summary>
/// A sink that fans NMEA sentences out to every connected client over some transport. The
/// sink-side counterpart of <c>Ais.Net.Receiver.Receiver.INmeaReceiver</c>: a broadcaster accepts
/// one sentence at a time and takes care of listening, client lifetimes, line framing and
/// per-client backpressure.
/// </summary>
public interface INmeaBroadcaster : IAsyncDisposable
{
    /// <summary>
    /// Gets a short description of the listener for logs and the console, such as
    /// <c>tcp://0.0.0.0:5631</c> or <c>ws://0.0.0.0:8080/</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the number of clients currently connected.
    /// </summary>
    int ClientCount { get; }

    /// <summary>
    /// Starts listening. Returns once the listener is bound, so a caller may connect immediately.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the listener is accepting clients.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Queues one sentence, without any line terminator, for delivery to every connected client.
    /// The broadcaster copies the bytes before returning, so the caller may reuse its buffer. A
    /// client that cannot keep up is handled per the broadcaster's options rather than by blocking
    /// this call; with no clients connected the sentence is dropped.
    /// </summary>
    /// <param name="sentence">The sentence bytes, excluding the line terminator.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the sentence has been queued.</returns>
    ValueTask BroadcastAsync(ReadOnlyMemory<byte> sentence, CancellationToken cancellationToken);

    /// <summary>
    /// Closes every connected client while continuing to listen, so a receiver under test sees a
    /// dropped feed and must reconnect. Used by fault injection.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the clients have been closed.</returns>
    Task DisconnectAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops listening and closes every connected client. Safe to call more than once.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the listener has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}
