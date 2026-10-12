// <copyright file="Log.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Broadcaster.Telemetry;

/// <summary>
/// Source-generated logging methods for the broadcaster transports: <see cref="TcpNmeaBroadcaster"/>
/// (event ids 5000-5049) and <see cref="WebSocketNmeaBroadcaster"/> (event ids 5050-5099).
/// </summary>
internal static partial class Log
{
    // TCP broadcaster events (5000-5049)
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Information,
        Message = "TCP broadcaster listening on {Endpoint}")]
    public static partial void TcpListening(this ILogger logger, string endpoint);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "TCP client {RemoteEndpoint} connected ({ClientCount} clients)")]
    public static partial void TcpClientConnected(this ILogger logger, string remoteEndpoint, int clientCount);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Information,
        Message = "TCP client {RemoteEndpoint} disconnected ({ClientCount} clients)")]
    public static partial void TcpClientDisconnected(this ILogger logger, string remoteEndpoint, int clientCount);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Warning,
        Message = "TCP client {RemoteEndpoint} too slow: its queue of {Capacity} sentences is full, disconnecting")]
    public static partial void TcpClientTooSlow(this ILogger logger, string remoteEndpoint, int capacity);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Debug,
        Message = "TCP client {RemoteEndpoint} write failed")]
    public static partial void TcpClientWriteError(this ILogger logger, Exception exception, string remoteEndpoint);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Information,
        Message = "TCP broadcaster on {Endpoint} stopped")]
    public static partial void TcpStopped(this ILogger logger, string endpoint);

    [LoggerMessage(
        EventId = 5006,
        Level = LogLevel.Warning,
        Message = "TCP broadcaster on {Endpoint} failed to accept a client")]
    public static partial void TcpAcceptError(this ILogger logger, Exception exception, string endpoint);

    [LoggerMessage(
        EventId = 5007,
        Level = LogLevel.Information,
        Message = "TCP broadcaster disconnecting {ClientCount} clients")]
    public static partial void TcpDisconnectingAll(this ILogger logger, int clientCount);

    // WebSocket broadcaster events (5050-5099)
    [LoggerMessage(
        EventId = 5050,
        Level = LogLevel.Information,
        Message = "WebSocket broadcaster listening on {Url}")]
    public static partial void WebSocketListening(this ILogger logger, string url);

    [LoggerMessage(
        EventId = 5051,
        Level = LogLevel.Information,
        Message = "WebSocket client {RemoteEndpoint} connected ({ClientCount} clients)")]
    public static partial void WebSocketClientConnected(this ILogger logger, string remoteEndpoint, int clientCount);

    [LoggerMessage(
        EventId = 5052,
        Level = LogLevel.Information,
        Message = "WebSocket client {RemoteEndpoint} disconnected ({ClientCount} clients)")]
    public static partial void WebSocketClientDisconnected(this ILogger logger, string remoteEndpoint, int clientCount);

    [LoggerMessage(
        EventId = 5053,
        Level = LogLevel.Warning,
        Message = "WebSocket client {RemoteEndpoint} too slow: its queue of {Capacity} sentences is full, disconnecting")]
    public static partial void WebSocketClientTooSlow(this ILogger logger, string remoteEndpoint, int capacity);

    [LoggerMessage(
        EventId = 5054,
        Level = LogLevel.Debug,
        Message = "WebSocket client {RemoteEndpoint} send failed")]
    public static partial void WebSocketClientWriteError(this ILogger logger, Exception exception, string remoteEndpoint);

    [LoggerMessage(
        EventId = 5055,
        Level = LogLevel.Information,
        Message = "WebSocket broadcaster on {Url} stopped")]
    public static partial void WebSocketStopped(this ILogger logger, string url);

    [LoggerMessage(
        EventId = 5056,
        Level = LogLevel.Debug,
        Message = "WebSocket broadcaster rejected a non-WebSocket request for {Path} from {RemoteEndpoint}")]
    public static partial void WebSocketRequestRejected(this ILogger logger, string path, string remoteEndpoint);

    [LoggerMessage(
        EventId = 5057,
        Level = LogLevel.Information,
        Message = "WebSocket broadcaster disconnecting {ClientCount} clients")]
    public static partial void WebSocketDisconnectingAll(this ILogger logger, int clientCount);
}
