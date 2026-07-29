// <copyright file="IAisConnectionMonitor.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Health;

/// <summary>
/// Interface for monitoring AIS connection status.
/// </summary>
public interface IAisConnectionMonitor
{
    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    /// <returns>The current connection status.</returns>
    ConnectionStatus GetStatus();

    /// <summary>
    /// Records that a message was received.
    /// </summary>
    void RecordMessageReceived();

    /// <summary>
    /// Records that the connection state changed.
    /// </summary>
    /// <param name="isConnected">Whether currently connected.</param>
    void RecordConnectionStateChanged(bool isConnected);
}

/// <summary>
/// Represents the current connection status.
/// </summary>
/// <param name="IsConnected">Whether currently connected to the AIS data source.</param>
/// <param name="LastMessageTime">The time the last message was received, if any.</param>
/// <param name="TimeSinceLastMessage">Time elapsed since the last message was received.</param>
/// <param name="TotalMessagesReceived">Total number of messages received since startup.</param>
public record ConnectionStatus(
    bool IsConnected,
    DateTimeOffset? LastMessageTime,
    TimeSpan TimeSinceLastMessage,
    long TotalMessagesReceived);
