// <copyright file="Log.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Storage;

/// <summary>
/// Source-generated logging methods for resilient storage.
/// </summary>
internal static partial class Log
{
    // Storage resilience events (6000-6009)
    [LoggerMessage(
        EventId = 6000,
        Level = LogLevel.Warning,
        Message = "Storage batch of {MessageCount} sentences dead-lettered to {DeadLetterFile} after write retries were exhausted")]
    public static partial void StorageBatchDeadLettered(this ILogger logger, int messageCount, string deadLetterFile, Exception cause);

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Information,
        Message = "Replaying {PendingCount} dead-lettered batch(es) to storage")]
    public static partial void DeadLetterReplayStarting(this ILogger logger, int pendingCount);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Replayed dead-lettered batch {DeadLetterFile} ({MessageCount} sentences) to storage")]
    public static partial void DeadLetterReplayed(this ILogger logger, string deadLetterFile, int messageCount);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Warning,
        Message = "Dead-letter replay deferred; storage still unavailable, {RemainingCount} batch(es) left for the next sweep")]
    public static partial void DeadLetterReplayDeferred(this ILogger logger, int remainingCount, Exception cause);

    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Warning,
        Message = "Could not read dead-letter file {DeadLetterFile}; skipping it")]
    public static partial void DeadLetterReadFailed(this ILogger logger, string deadLetterFile, Exception cause);

    [LoggerMessage(
        EventId = 6005,
        Level = LogLevel.Error,
        Message = "Dead-letter replay sweep failed unexpectedly")]
    public static partial void DeadLetterReplaySweepFailed(this ILogger logger, Exception cause);

    [LoggerMessage(
        EventId = 6006,
        Level = LogLevel.Warning,
        Message = "Timed out waiting for the in-flight dead-letter replay sweep to stop during shutdown")]
    public static partial void DeadLetterReplayStopTimedOut(this ILogger logger);
}
