// <copyright file="NullAisMessagePublisher.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.Receiver.Host.Worker.Publishing;

/// <summary>
/// The publisher used when no message bus is configured - the worker's normal standalone shape under
/// Docker or systemd.
/// </summary>
public sealed class NullAisMessagePublisher : IAisMessagePublisher
{
    /// <inheritdoc/>
    public void Publish(IAisMessage message)
    {
    }
}
