// <copyright file="AisNats.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Hosting;

/// <summary>
/// The NATS names shared between the worker, which publishes, and whatever subscribes - today the
/// visualizer demo's page, which learns them from <c>/api/config</c>.
/// </summary>
/// <remarks>
/// A constant rather than configuration, deliberately: publisher and subscriber live in different
/// processes with no shared settings file, so a knob on one side is a setting that can do nothing
/// except silently break the other. This project is referenced by both hosts, which makes it the one
/// place a shared name can live.
/// </remarks>
public static class AisNats
{
    /// <summary>
    /// The subject decoded AIS messages are published on, as polymorphic JSON
    /// (<c>Ais.Net.Models.Json</c>, with a <c>$type</c> discriminator).
    /// </summary>
    public const string MessagesSubject = "ais.messages";

    /// <summary>
    /// The header carrying the Unix time in seconds that a message's NMEA tag block gives for it. It
    /// is absent when the sentence had no timestamp. For a replayed recording this is the time of the
    /// capture, not the time of the broadcast.
    /// </summary>
    public const string TimestampHeader = "Ais-Timestamp";
}
