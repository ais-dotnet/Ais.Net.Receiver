// <copyright file="StorageConfigValidator.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Options;

namespace Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

/// <summary>
/// Validates <see cref="StorageConfig"/>. <see cref="StorageConfig.ConnectionString"/> and
/// <see cref="StorageConfig.ContainerName"/> are required only when
/// <see cref="StorageConfig.EnableCapture"/> is <see langword="true"/>, so the receiver can start for
/// live display with no storage configured instead of failing validation at startup.
/// </summary>
public sealed class StorageConfigValidator : IValidateOptions<StorageConfig>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, StorageConfig options)
    {
        if (!options.EnableCapture)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("Storage:ConnectionString is required when Storage:EnableCapture is true.");
        }

        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            failures.Add("Storage:ContainerName is required when Storage:EnableCapture is true.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
