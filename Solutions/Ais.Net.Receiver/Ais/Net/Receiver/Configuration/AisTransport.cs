// <copyright file="AisTransport.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Configuration;

/// <summary>
/// Identifies the network transport used to receive NMEA sentences.
/// </summary>
public enum AisTransport
{
    /// <summary>
    /// A raw TCP stream, configured with <see cref="AisConnectionConfig.Host"/> and
    /// <see cref="AisConnectionConfig.Port"/>. This is the default.
    /// </summary>
    Tcp = 0,

    /// <summary>
    /// A WebSocket connection, configured with an absolute <c>ws://</c> or <c>wss://</c>
    /// <see cref="AisConnectionConfig.Url"/>.
    /// </summary>
    WebSocket = 1,
}
