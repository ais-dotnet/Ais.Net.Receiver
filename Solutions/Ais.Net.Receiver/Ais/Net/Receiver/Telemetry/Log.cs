// <copyright file="Log.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Telemetry;

/// <summary>
/// Source-generated logging methods for the receiver component.
/// </summary>
internal static partial class Log
{
    // Connection events (3000-3009)
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Connecting to {Host}:{Port}")]
    public static partial void Connecting(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Connected to {Host}:{Port}")]
    public static partial void Connected(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "Connection lost to {Host}:{Port}, retrying in {DelayMs}ms (attempt {Attempt})")]
    public static partial void ConnectionLost(this ILogger logger, string host, int port, long delayMs, int attempt);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Error,
        Message = "Connection failed to {Host}:{Port}")]
    public static partial void ConnectionFailed(this ILogger logger, Exception exception, string host, int port);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Debug,
        Message = "Idle timeout triggered, reconnecting")]
    public static partial void IdleTimeout(this ILogger logger);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Debug,
        Message = "Processing message type {MessageType} from MMSI {Mmsi}")]
    public static partial void ProcessingMessage(this ILogger logger, int messageType, uint mmsi);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Received NMEA sentence: {Sentence}")]
    public static partial void ReceivedSentence(this ILogger logger, string sentence);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Warning,
        Message = "Parse error for line: {Line}")]
    public static partial void ParseError(this ILogger logger, Exception exception, string line);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Warning,
        Message = "Unsupported message type encountered: {MessageType}")]
    public static partial void UnsupportedMessageType(this ILogger logger, int messageType);

    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Information,
        Message = "Receiver starting with retry periodicity {RetryPeriodicity} and {RetryAttempts} attempts")]
    public static partial void ReceiverStarting(this ILogger logger, TimeSpan retryPeriodicity, int retryAttempts);

    // Network stream receiver events (4000-4009)
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Information,
        Message = "Connecting to AIS stream at {Host}:{Port}")]
    public static partial void StreamConnecting(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Connected to AIS stream at {Host}:{Port}")]
    public static partial void StreamConnected(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Connection attempt {Attempt} failed for {Host}:{Port}, retrying in {DelayMs}ms")]
    public static partial void StreamConnectionRetry(this ILogger logger, int attempt, string host, int port, long delayMs);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Warning,
        Message = "Stream disconnected from {Host}:{Port}, will attempt to reconnect")]
    public static partial void StreamDisconnected(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Debug,
        Message = "Idle timeout after {TimeoutSeconds}s, reconnecting to {Host}:{Port}")]
    public static partial void StreamIdleTimeout(this ILogger logger, double timeoutSeconds, string host, int port);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Error,
        Message = "Stream connection error to {Host}:{Port}")]
    public static partial void StreamConnectionError(this ILogger logger, Exception exception, string host, int port);

    // TCP client stream reader events (4010-4019)
    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Debug,
        Message = "TCP connecting to {Host}:{Port}")]
    public static partial void TcpConnecting(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Debug,
        Message = "TCP connected to {Host}:{Port}, pipe reader initialized")]
    public static partial void TcpConnected(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 4012,
        Level = LogLevel.Debug,
        Message = "TCP connection closed to {Host}:{Port}")]
    public static partial void TcpDisconnected(this ILogger logger, string host, int port);

    [LoggerMessage(
        EventId = 4013,
        Level = LogLevel.Error,
        Message = "TCP connection failed to {Host}:{Port}")]
    public static partial void TcpConnectionFailed(this ILogger logger, Exception exception, string host, int port);

    [LoggerMessage(
        EventId = 4014,
        Level = LogLevel.Warning,
        Message = "Discarded an over-long NMEA line ({Length} bytes with no newline, limit {Limit}); resyncing on the next newline")]
    public static partial void NmeaLineDiscarded(this ILogger logger, int length, int limit);
}
