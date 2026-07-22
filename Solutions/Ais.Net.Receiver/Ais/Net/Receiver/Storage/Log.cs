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
}
