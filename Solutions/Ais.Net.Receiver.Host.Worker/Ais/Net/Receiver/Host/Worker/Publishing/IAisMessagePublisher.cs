// <copyright file="IAisMessagePublisher.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.Receiver.Host.Worker.Publishing;

/// <summary>
/// Forwards decoded AIS messages to somewhere outside this process. The worker always has one of
/// these; when no message bus is configured it is the no-op implementation, so the receive path does
/// not need to know whether publishing is switched on.
/// </summary>
public interface IAisMessagePublisher
{
    /// <summary>
    /// Offers a message for publication. Implementations must not block the caller: this runs on the
    /// receive path, where a slow publisher would stall decoding for every other subscriber.
    /// </summary>
    /// <param name="message">The decoded message.</param>
    void Publish(IAisMessage message);
}
