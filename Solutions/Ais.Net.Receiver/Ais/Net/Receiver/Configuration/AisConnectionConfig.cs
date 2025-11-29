// <copyright file="AisConnectionConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Ais.Net.Receiver.Configuration;

public record AisConnectionConfig
{
    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; }

    public AisRetryConfig Retry { get; set; } = new();
}