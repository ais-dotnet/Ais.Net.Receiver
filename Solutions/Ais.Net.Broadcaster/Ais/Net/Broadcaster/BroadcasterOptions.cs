// <copyright file="BroadcasterOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net;

namespace Ais.Net.Broadcaster;

/// <summary>
/// The line terminator written after each sentence.
/// </summary>
public enum NmeaLineEnding
{
    /// <summary>A bare line feed.</summary>
    Lf,

    /// <summary>Carriage return and line feed, as the Norwegian Coastal Administration feed sends.</summary>
    CrLf,
}

/// <summary>
/// What a broadcaster does with a client whose send queue is full.
/// </summary>
public enum SlowClientPolicy
{
    /// <summary>Drop the oldest queued sentences so the client stays connected but skips ahead.</summary>
    DropOldest,

    /// <summary>Disconnect the client, as Open Waters does with "client too slow".</summary>
    Disconnect,
}

/// <summary>
/// Settings shared by every broadcaster transport.
/// </summary>
public abstract record BroadcasterOptions
{
    /// <summary>Gets the address to bind. Defaults to all interfaces.</summary>
    public IPAddress Address { get; init; } = IPAddress.Any;

    /// <summary>Gets the port to listen on. 0 picks a free port, which tests use.</summary>
    public int Port { get; init; }

    /// <summary>Gets the line terminator written after each sentence. Defaults to CRLF.</summary>
    public NmeaLineEnding LineEnding { get; init; } = NmeaLineEnding.CrLf;

    /// <summary>Gets the number of sentences queued per client before <see cref="SlowClient"/> applies.</summary>
    public int ClientQueueCapacity { get; init; } = 4096;

    /// <summary>Gets what happens to a client whose queue is full. Defaults to dropping the oldest sentences.</summary>
    public SlowClientPolicy SlowClient { get; init; } = SlowClientPolicy.DropOldest;
}

/// <summary>
/// Settings for <see cref="TcpNmeaBroadcaster"/>: newline-terminated sentences on a raw TCP
/// socket, the shape of the Norwegian Coastal Administration feed.
/// </summary>
public sealed record TcpBroadcasterOptions : BroadcasterOptions
{
}

/// <summary>
/// How sentences are framed into WebSocket messages.
/// </summary>
public enum WebSocketFraming
{
    /// <summary>One sentence per text message, with no line terminator.</summary>
    SentencePerMessage,

    /// <summary>
    /// Several newline-terminated sentences per text message, flushed when
    /// <see cref="WebSocketBroadcasterOptions.BatchSize"/> sentences are queued or
    /// <see cref="WebSocketBroadcasterOptions.BatchInterval"/> has elapsed, whichever comes first.
    /// </summary>
    NewlineDelimitedBatch,
}

/// <summary>
/// Settings for <see cref="WebSocketNmeaBroadcaster"/>: sentences as WebSocket text messages from a
/// minimal Kestrel endpoint.
/// </summary>
public sealed record WebSocketBroadcasterOptions : BroadcasterOptions
{
    /// <summary>Gets the request path clients connect to. Defaults to <c>/</c>.</summary>
    public string Path { get; init; } = "/";

    /// <summary>Gets how sentences are framed into messages. Defaults to one sentence per message.</summary>
    public WebSocketFraming Framing { get; init; } = WebSocketFraming.SentencePerMessage;

    /// <summary>Gets the sentences per message when batching.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Gets the longest a partial batch waits before it is sent.</summary>
    public TimeSpan BatchInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}
