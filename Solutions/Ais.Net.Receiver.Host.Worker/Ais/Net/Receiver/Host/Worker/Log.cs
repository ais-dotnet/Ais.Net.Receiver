// <copyright file="Log.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Host.Worker;

/// <summary>
/// Source-generated logging methods for the Worker service.
/// </summary>
internal static partial class Log
{
    // Lifecycle events (1000-1009)
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Worker starting - initializing components")]
    public static partial void WorkerStarting(this ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Worker initialization complete")]
    public static partial void WorkerInitializationComplete(this ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Worker started")]
    public static partial void WorkerStarted(this ILogger logger);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Worker stopping")]
    public static partial void WorkerStopping(this ILogger logger);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Worker stopped - completing dataflow pipeline")]
    public static partial void WorkerStopped(this ILogger logger);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Worker execution cancelled")]
    public static partial void WorkerCancelled(this ILogger logger);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Critical,
        Message = "ReceiverHost not initialized - cannot execute")]
    public static partial void ReceiverHostNotInitialized(this ILogger logger);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Information,
        Message = "Storage flush completed")]
    public static partial void StorageFlushCompleted(this ILogger logger);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Warning,
        Message = "Storage flush timed out during shutdown")]
    public static partial void StorageFlushTimedOut(this ILogger logger);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Error,
        Message = "Storage flush failed with an error")]
    public static partial void StorageFlushError(this ILogger logger, Exception exception);

    // Statistics events (1100-1109)
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "{Timestamp:s}: Sentences: {Sentences} | Messages: {Messages} | Errors: {Errors}")]
    public static partial void StreamStatistics(this ILogger logger, DateTime timestamp, long sentences, long messages, long errors);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Error,
        Message = "Error in statistics stream")]
    public static partial void StatisticsStreamError(this ILogger logger, Exception exception);

    // Vessel events (1200-1209)
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "[{Mmsi}: '{VesselName}'] - [{Position}] - [{CourseOverGround}]")]
    public static partial void VesselNavigation(this ILogger logger, uint mmsi, string vesselName, string position, double courseOverGround);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Information,
        Message = "{Sentence}")]
    public static partial void SentenceReceived(this ILogger logger, string sentence);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Information,
        Message = "{Message}")]
    public static partial void MessageReceived(this ILogger logger, string message);

    // Error events (1300-1309)
    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Error,
        Message = "Error received: {ErrorMessage}")]
    public static partial void ErrorReceived(this ILogger logger, string errorMessage);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Error,
        Message = "Bad line: {Line}")]
    public static partial void BadLine(this ILogger logger, string line);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Error,
        Message = "Storage persistence failed")]
    public static partial void StoragePersistenceFailed(this ILogger logger, Exception exception);
}