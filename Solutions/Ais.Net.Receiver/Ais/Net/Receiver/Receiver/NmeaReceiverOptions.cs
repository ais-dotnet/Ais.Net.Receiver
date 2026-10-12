// <copyright file="NmeaReceiverOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Reconnect and idle-detection settings for an <see cref="NmeaReceiver"/>. These apply only to
/// <see cref="NmeaSourceKind.Live"/> connections; a <see cref="NmeaSourceKind.Finite"/> source is
/// read once and never retried.
/// </summary>
public sealed record NmeaReceiverOptions
{
    /// <summary>
    /// Gets the base reconnect delay. The linear backoff waits this long multiplied by the
    /// (capped) attempt count. Defaults to one second.
    /// </summary>
    public TimeSpan RetryPeriodicity { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the attempt count at which the linear reconnect backoff stops growing. Defaults to 100.
    /// </summary>
    public int RetryAttemptLimit { get; init; } = 100;

    /// <summary>
    /// Gets how long a connected feed may be silent before the receiver gives up on it and
    /// reconnects. <see langword="null"/> (or a non-positive value) means the 60 second default,
    /// which detects a silently dropped (half-open) connection without being so short that a
    /// legitimately quiet feed churns reconnects.
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; }
}
