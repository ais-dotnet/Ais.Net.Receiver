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
}
