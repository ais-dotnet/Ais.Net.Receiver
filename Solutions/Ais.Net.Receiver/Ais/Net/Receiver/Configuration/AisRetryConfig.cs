// <copyright file="AisRetryConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Ais.Net.Receiver.Configuration;

/// <summary>
/// A retry policy: how many attempts to make and the base delay between them.
/// </summary>
public record AisRetryConfig : IValidatableObject
{
    /// <summary>
    /// Gets or sets the number of attempts, at least one.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the base delay between attempts. Must be greater than zero: the receiver's
    /// backoff multiplies it, so a zero value would turn reconnection into a hot loop.
    /// </summary>
    public TimeSpan Periodicity { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.Periodicity <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "The Periodicity field must be greater than zero.",
                [nameof(this.Periodicity)]);
        }
    }
}
