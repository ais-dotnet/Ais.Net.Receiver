// <copyright file="AisConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System;

namespace Ais.Net.Receiver.Configuration;

public class AisConfig
{
    public required string Host { get; set; }

    public LoggerVerbosity LoggerVerbosity { get; set; }

    public TimeSpan StatisticsPeriodicity { get; set; }

    public int Port { get; set; }

    public int RetryAttempts { get; set; }

    public TimeSpan RetryPeriodicity { get; set; }
}