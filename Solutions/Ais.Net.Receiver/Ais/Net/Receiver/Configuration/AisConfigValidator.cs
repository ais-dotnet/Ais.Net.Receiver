// <copyright file="AisConfigValidator.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace Ais.Net.Receiver.Configuration;

/// <summary>
/// Validates the nested sections of <see cref="AisConfig"/> at options-validation time.
/// </summary>
/// <remarks>
/// <c>ValidateDataAnnotations</c> inspects
/// only the top-level object, so the data annotations and the <see cref="IValidatableObject"/>
/// rules on <see cref="AisConnectionConfig"/> and the retry sections would otherwise never run at
/// start-up, and a bad connection setting would surface only when the receiver is created.
/// Registering this validator alongside <c>ValidateOnStart</c> makes the host fail fast with the
/// section and member names instead.
/// </remarks>
public sealed class AisConfigValidator : IValidateOptions<AisConfig>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, AisConfig options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        ValidateSection("Ais:Connection", options.Connection, failures);
        ValidateSection("Ais:Connection:Retry", options.Connection?.Retry, failures);
        ValidateSection("Ais:Receiver", options.Receiver, failures);
        ValidateSection("Ais:Receiver:Retry", options.Receiver?.Retry, failures);
        ValidateSection("Ais:Telemetry", options.Telemetry, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateSection(string path, object? section, List<string> failures)
    {
        if (section is null)
        {
            failures.Add($"{path} is required.");
            return;
        }

        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(section, new ValidationContext(section), results, validateAllProperties: true))
        {
            return;
        }

        foreach (ValidationResult result in results)
        {
            string members = string.Join(", ", result.MemberNames.Select(member => $"{path}:{member}"));
            failures.Add(members.Length > 0
                ? $"{members}: {result.ErrorMessage}"
                : $"{path}: {result.ErrorMessage}");
        }
    }
}
