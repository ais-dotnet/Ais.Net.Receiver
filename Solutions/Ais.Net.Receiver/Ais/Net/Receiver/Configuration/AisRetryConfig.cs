// <copyright file="AisRetryConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Ais.Net.Receiver.Configuration;

public record AisRetryConfig
{
    [Range(1, int.MaxValue)]
    public int Attempts { get; set; }

    public TimeSpan Periodicity { get; set; }
}