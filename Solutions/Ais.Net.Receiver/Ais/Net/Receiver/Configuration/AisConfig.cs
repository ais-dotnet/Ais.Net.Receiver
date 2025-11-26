// <copyright file="AisConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Configuration;

public class AisConfig
{
    [Required]
    public required string Host { get; set; }

    public LogLevel LoggerVerbosity { get; set; } = LogLevel.None;

    public TimeSpan StatisticsPeriodicity { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; }

    [Range(1, int.MaxValue)]
    public int RetryAttempts { get; set; }

    public TimeSpan RetryPeriodicity { get; set; }

    public TimeSpan? VesselInactivityTimeout { get; set; }
}