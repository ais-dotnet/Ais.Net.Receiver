// <copyright file="BroadcastPipelineOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Broadcaster;

/// <summary>
/// How the pipeline spaces sentences out in time.
/// </summary>
public enum PacingMode
{
    /// <summary>As fast as the source delivers them. Right for relaying a live feed.</summary>
    None,

    /// <summary>A fixed number of sentences per second.</summary>
    Fixed,

    /// <summary>
    /// Reproduce the original timing from each sentence's NMEA tag-block <c>c:</c> timestamp, so a
    /// recording replays with its real bursts and lulls. Sentences without a timestamp are sent
    /// immediately.
    /// </summary>
    TagBlockTimestamps,
}

/// <summary>
/// Faults the pipeline injects so a receiver's resilience paths can be exercised on demand. Every
/// setting is off by default.
/// </summary>
public sealed record FaultInjectionOptions
{
    /// <summary>Gets how often every connected client is dropped, so receivers must reconnect.</summary>
    public TimeSpan? DisconnectEvery { get; init; }

    /// <summary>Gets how often the feed goes silent while clients stay connected, so idle timeouts fire.</summary>
    public TimeSpan? SilenceEvery { get; init; }

    /// <summary>Gets how long each silence lasts.</summary>
    public TimeSpan SilenceDuration { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Gets how often a malformed line (bad checksum, truncated payload) is sent in place of a sentence.</summary>
    public int? MalformedEveryN { get; init; }

    /// <summary>Gets how often a line longer than the receivers' 8 KiB cap is sent in place of a sentence.</summary>
    public int? OverLongEveryN { get; init; }
}

/// <summary>
/// Settings for <see cref="BroadcastPipeline"/>.
/// </summary>
public sealed record BroadcastPipelineOptions
{
    /// <summary>Gets the pacing mode. Defaults to none.</summary>
    public PacingMode Pacing { get; init; } = PacingMode.None;

    /// <summary>Gets the rate for <see cref="PacingMode.Fixed"/>.</summary>
    public double SentencesPerSecond { get; init; } = 100;

    /// <summary>Gets the speed multiplier for <see cref="PacingMode.TagBlockTimestamps"/>; 2 replays twice as fast.</summary>
    public double SpeedFactor { get; init; } = 1;

    /// <summary>Gets the longest gap honoured between tag-block timestamps, so a recording with a hole does not stall.</summary>
    public TimeSpan MaxTagBlockGap { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets a value indicating whether a finite source is re-enumerated from the start when it ends.</summary>
    public bool Loop { get; init; }

    /// <summary>Gets the fault injection settings.</summary>
    public FaultInjectionOptions Faults { get; init; } = new();
}
