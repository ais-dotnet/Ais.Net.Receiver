// <copyright file="AisConnectionConfig.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

namespace Ais.Net.Receiver.Configuration;

/// <summary>
/// Describes how to connect to the source of NMEA sentences, bound from <c>Ais:Connection</c>.
/// </summary>
/// <remarks>
/// The settings that are required depend on <see cref="Transport"/>: <see cref="Host"/> and
/// <see cref="Port"/> are required for <see cref="AisTransport.Tcp"/>, and <see cref="Url"/> is
/// required for <see cref="AisTransport.WebSocket"/>. The checks are made in
/// <see cref="Validate(ValidationContext)"/> rather than with attributes, because attributes
/// cannot express a requirement that depends on another property.
/// </remarks>
public record AisConnectionConfig : IValidatableObject
{
    /// <summary>
    /// Gets or sets the transport used to receive NMEA sentences. Defaults to
    /// <see cref="AisTransport.Tcp"/>, so configuration that predates this setting is unchanged.
    /// </summary>
    public AisTransport Transport { get; set; } = AisTransport.Tcp;

    /// <summary>
    /// Gets or sets the host name or IP address of the TCP source. Required when
    /// <see cref="Transport"/> is <see cref="AisTransport.Tcp"/>; ignored otherwise.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port of the TCP source, in the range 1-65535. Required when
    /// <see cref="Transport"/> is <see cref="AisTransport.Tcp"/>; ignored otherwise.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the absolute <c>ws://</c> or <c>wss://</c> URL of the WebSocket source.
    /// Required when <see cref="Transport"/> is <see cref="AisTransport.WebSocket"/>; ignored
    /// otherwise.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the policy for reconnecting after the connection fails. It applies to every
    /// transport.
    /// </summary>
    public AisRetryConfig Retry { get; set; } = new();

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        switch (this.Transport)
        {
            case AisTransport.Tcp:
                if (string.IsNullOrWhiteSpace(this.Host))
                {
                    yield return new ValidationResult(
                        "The Host field is required when Transport is Tcp.",
                        [nameof(this.Host)]);
                }

                if (this.Port is < 1 or > 65535)
                {
                    yield return new ValidationResult(
                        "The Port field must be between 1 and 65535 when Transport is Tcp.",
                        [nameof(this.Port)]);
                }

                break;

            case AisTransport.WebSocket:
                if (string.IsNullOrWhiteSpace(this.Url))
                {
                    yield return new ValidationResult(
                        "The Url field is required when Transport is WebSocket.",
                        [nameof(this.Url)]);
                }
                else if (!Uri.TryCreate(this.Url, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss))
                {
                    yield return new ValidationResult(
                        "The Url field must be an absolute ws:// or wss:// URL when Transport is WebSocket.",
                        [nameof(this.Url)]);
                }

                break;

            default:
                yield return new ValidationResult(
                    $"The Transport value '{this.Transport}' is not supported.",
                    [nameof(this.Transport)]);
                break;
        }
    }
}
