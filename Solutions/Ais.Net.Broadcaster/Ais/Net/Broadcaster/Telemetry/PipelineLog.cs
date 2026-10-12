// <copyright file="PipelineLog.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Broadcaster.Telemetry;

/// <summary>
/// Source-generated logging methods for <see cref="BroadcastPipeline"/> (event ids 5100-5149).
/// </summary>
internal static partial class PipelineLog
{
    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Information,
        Message = "Broadcast pipeline started with sinks {Sinks} and pacing {Pacing}")]
    public static partial void PipelineStarted(this ILogger logger, string sinks, PacingMode pacing);

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Information,
        Message = "Source ended after {SentencesBroadcast} sentences")]
    public static partial void SourceEnded(this ILogger logger, long sentencesBroadcast);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Information,
        Message = "Looping source from the start (loop {Loop})")]
    public static partial void Looping(this ILogger logger, int loop);

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Information,
        Message = "Fault injected: {FaultKind}")]
    public static partial void FaultInjected(this ILogger logger, string faultKind);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Information,
        Message = "Broadcast pipeline stopped: {SentencesBroadcast} sentences, {FaultsInjected} faults, {Loops} loops")]
    public static partial void PipelineStopped(this ILogger logger, long sentencesBroadcast, long faultsInjected, int loops);

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Error,
        Message = "Broadcast pipeline source failed")]
    public static partial void SourceError(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5106,
        Level = LogLevel.Warning,
        Message = "Stopping sink {Sink} failed")]
    public static partial void SinkStopFailed(this ILogger logger, Exception exception, string sink);

    [LoggerMessage(
        EventId = 5107,
        Level = LogLevel.Warning,
        Message = "Source produced no sentences; not looping an empty source")]
    public static partial void EmptySourceNotLooped(this ILogger logger);
}
