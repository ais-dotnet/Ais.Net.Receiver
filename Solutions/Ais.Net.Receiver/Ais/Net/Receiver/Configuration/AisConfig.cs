// <copyright file="AisConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Configuration;

public record AisConfig
{
    public AisConnectionConfig Connection { get; set; } = new();

    public AisReceiverConfig Receiver { get; set; } = new();

    public AisTelemetryConfig Telemetry { get; set; } = new();
}