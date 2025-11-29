// <copyright file="AisReceiverConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Configuration;

public record AisReceiverConfig
{
    public AisRetryConfig Retry { get; set; } = new();
}