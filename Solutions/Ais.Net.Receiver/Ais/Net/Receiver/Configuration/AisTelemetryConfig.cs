// <copyright file="AisTelemetryConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Configuration;

public record AisTelemetryConfig
{
    public LogLevel Verbosity { get; set; } = LogLevel.None;

    public TimeSpan StatisticsPeriodicity { get; set; }

    public TimeSpan? VesselInactivityTimeout { get; set; }
}