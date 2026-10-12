// <copyright file="AisTelemetryConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Configuration;

/// <summary>
/// Controls what the hosts log about the stream, bound from <c>Ais:Telemetry</c>.
/// </summary>
public record AisTelemetryConfig : IValidatableObject
{
    /// <summary>
    /// Gets or sets the minimum level of stream detail to log. Defaults to <see cref="LogLevel.None"/>.
    /// </summary>
    public LogLevel Verbosity { get; set; } = LogLevel.None;

    /// <summary>
    /// Gets or sets the interval between stream statistics lines. Must be greater than zero.
    /// Defaults to one minute.
    /// </summary>
    public TimeSpan StatisticsPeriodicity { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how long a vessel may be silent before its cached name is forgotten. When set,
    /// it must be greater than zero; when unset, the receiver's default applies.
    /// </summary>
    public TimeSpan? VesselInactivityTimeout { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.StatisticsPeriodicity <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "The StatisticsPeriodicity field must be greater than zero.",
                [nameof(this.StatisticsPeriodicity)]);
        }

        if (this.VesselInactivityTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "The VesselInactivityTimeout field must be greater than zero when set.",
                [nameof(this.VesselInactivityTimeout)]);
        }
    }
}
