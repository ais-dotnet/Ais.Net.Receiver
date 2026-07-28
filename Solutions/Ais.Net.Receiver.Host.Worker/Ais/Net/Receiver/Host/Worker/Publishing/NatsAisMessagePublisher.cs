// <copyright file="NatsAisMessagePublisher.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Threading.Channels;

using Ais.Net.Models;
using Ais.Net.Models.Abstractions;
using Ais.Net.Models.Json.Nats;
using Ais.Net.Receiver.Hosting;

using NATS.Client.Core;

namespace Ais.Net.Receiver.Host.Worker.Publishing;

/// <summary>
/// Publishes decoded AIS messages to a NATS subject as polymorphic JSON, so consumers outside this
/// process - the visualiser demo, for one - can subscribe to the live feed without opening their own
/// connection to the AIS network.
/// </summary>
/// <remarks>
/// <para>
/// Messages are handed to a bounded channel rather than published inline. <see cref="Publish"/> runs
/// on the receive path, where awaiting a network round trip would stall decoding for every other
/// subscriber, and spawning a task per message would let a stalled broker consume memory without
/// limit. When the channel is full the oldest queued message is dropped: for a position feed, stale
/// positions are the right thing to lose, and the loss is counted and logged rather than hidden.
/// </para>
/// <para>
/// Serialization uses <see cref="AisMessageNatsSerializer"/>, so the <c>$type</c> discriminator lets a
/// subscriber reconstruct the concrete message type.
/// </para>
/// </remarks>
public sealed class NatsAisMessagePublisher : IAisMessagePublisher, IHostedService, IAsyncDisposable
{
    private readonly INatsConnection connection;
    private readonly ILogger<NatsAisMessagePublisher> logger;
    private readonly string subject;
    private readonly Channel<AisMessageBase> queue;
    private readonly CancellationTokenSource stopping = new();

    private Task? drainTask;
    private long dropped;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsAisMessagePublisher"/> class.
    /// </summary>
    /// <param name="connection">The NATS connection.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="subject">The subject to publish to.</param>
    /// <param name="capacity">How many messages may be queued before the oldest are dropped.</param>
    public NatsAisMessagePublisher(
        INatsConnection connection,
        ILogger<NatsAisMessagePublisher> logger,
        string subject = AisNats.MessagesSubject,
        int capacity = 10_000)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        this.connection = connection;
        this.logger = logger;
        this.subject = subject;
        this.queue = Channel.CreateBounded<AisMessageBase>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    /// <inheritdoc/>
    public void Publish(IAisMessage message)
    {
        // The serializer is polymorphic over AisMessageBase; every message the decoder produces
        // derives from it, but the receiver's stream is typed as the interface.
        if (message is not AisMessageBase concrete)
        {
            return;
        }

        if (!this.queue.Writer.TryWrite(concrete))
        {
            this.CountDrop();
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.drainTask = Task.Run(() => this.DrainAsync(this.stopping.Token), CancellationToken.None);
        this.logger.NatsPublishingStarted(this.subject);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        this.queue.Writer.TryComplete();

        if (this.drainTask is not null)
        {
            // Give the queue a bounded chance to flush, then stop waiting: shutdown must not hang on
            // a broker that has gone away.
            Task completed = await Task.WhenAny(this.drainTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken))
                .ConfigureAwait(false);

            if (completed != this.drainTask)
            {
                await this.stopping.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.stopping.CancelAsync().ConfigureAwait(false);

        if (this.drainTask is not null)
        {
            try
            {
                await this.drainTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }

        this.stopping.Dispose();
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AisMessageBase message in this.queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await this.connection.PublishAsync(
                        this.subject,
                        message,
                        serializer: AisMessageNatsSerializer.Default,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One failed publish must not kill the loop: the broker may come back, and the
                    // feed keeps arriving either way.
                    this.logger.NatsPublishFailed(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void CountDrop()
    {
        long total = Interlocked.Increment(ref this.dropped);

        if (total == 1 || total % 10_000 == 0)
        {
            this.logger.NatsMessagesDropped(total);
        }
    }
}
