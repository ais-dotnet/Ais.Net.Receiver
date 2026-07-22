// <copyright file="AisConnectionMonitor.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Health;

/// <summary>
/// Default implementation of <see cref="IAisConnectionMonitor"/>.
/// </summary>
public class AisConnectionMonitor : IAisConnectionMonitor
{
    private readonly TimeProvider timeProvider;
    private readonly Lock lockObject = new();
    private volatile bool isConnected;
    private DateTimeOffset lastMessageTime;
    private long totalMessagesReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="AisConnectionMonitor"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public AisConnectionMonitor(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public ConnectionStatus GetStatus()
    {
        DateTimeOffset now = this.timeProvider.GetUtcNow();
        DateTimeOffset lastMessage;

        lock (this.lockObject)
        {
            lastMessage = this.lastMessageTime;
        }

        TimeSpan timeSinceLastMessage = lastMessage == default
            ? TimeSpan.MaxValue
            : now - lastMessage;

        return new ConnectionStatus(
            IsConnected: this.isConnected,
            LastMessageTime: lastMessage == default ? null : lastMessage,
            TimeSinceLastMessage: timeSinceLastMessage,
            TotalMessagesReceived: Interlocked.Read(ref this.totalMessagesReceived));
    }

    /// <inheritdoc/>
    public void RecordMessageReceived()
    {
        lock (this.lockObject)
        {
            this.lastMessageTime = this.timeProvider.GetUtcNow();
        }

        Interlocked.Increment(ref this.totalMessagesReceived);
    }

    /// <inheritdoc/>
    public void RecordConnectionStateChanged(bool connected)
    {
        this.isConnected = connected;
    }
}
