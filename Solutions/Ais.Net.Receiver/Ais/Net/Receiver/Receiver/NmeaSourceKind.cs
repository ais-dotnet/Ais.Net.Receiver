// <copyright file="NmeaSourceKind.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Describes what end of stream means for an <see cref="INmeaConnection"/>, and therefore how
/// <see cref="NmeaReceiver"/> drives it.
/// </summary>
public enum NmeaSourceKind
{
    /// <summary>
    /// A live feed (e.g. TCP or WebSocket). End of stream or a failure means the feed dropped, and
    /// the receiver reconnects with backoff.
    /// </summary>
    Live,

    /// <summary>
    /// A finite source (e.g. a file or a blob). End of stream means the data is exhausted and the
    /// receiver completes; a failure to open propagates to the caller.
    /// </summary>
    Finite,
}
