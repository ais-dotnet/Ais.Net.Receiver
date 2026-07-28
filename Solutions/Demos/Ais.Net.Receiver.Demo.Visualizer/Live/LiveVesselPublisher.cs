// <copyright file="LiveVesselPublisher.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Text.Json;

using Ais.Net.Models;
using Ais.Net.Models.Abstractions;
using Ais.Net.Models.Json.Nats;
using Ais.Net.Receiver.Demo.Tracks.Processing;

using Microsoft.Extensions.Options;

using NATS.Client.Core;

namespace Ais.Net.Receiver.Demo.Visualizer.Live;

/// <summary>
/// Turns the receiver's raw message stream into map-ready vessel updates: subscribes to the decoded
/// AIS messages the worker publishes, correlates each position with the vessel's name and ship type,
/// and republishes the result on its own subject for the browser.
/// </summary>
/// <remarks>
/// <para>
/// The correlation happens here, in C#, rather than in the page, because deriving a colour from a
/// position report needs the ship type from a different message and the AIS ship-type-to-category
/// mapping from <c>Ais.Net</c>. Doing it in the browser would mean reimplementing that mapping in
/// JavaScript and letting it drift; doing it here means live and replayed vessels are coloured by the
/// same <see cref="ShipTypeColors"/> code.
/// </para>
/// <para>
/// Position reports are published as they arrive rather than waiting for a matching static message.
/// A vessel transmits its name only every few minutes, so gating on one would leave the map empty for
/// a long time after startup; instead a vessel appears immediately under a placeholder name and gains
/// its real name and colour when the static message turns up.
/// </para>
/// </remarks>
public sealed class LiveVesselPublisher : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly int[] UnknownColor = [150, 249, 161];

    private readonly INatsConnection connection;
    private readonly VisualizerOptions options;
    private readonly ILogger<LiveVesselPublisher> logger;
    private readonly ConcurrentDictionary<uint, VesselIdentity> identities = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveVesselPublisher"/> class.
    /// </summary>
    /// <param name="connection">The NATS connection.</param>
    /// <param name="options">The visualiser options.</param>
    /// <param name="logger">The logger.</param>
    public LiveVesselPublisher(
        INatsConnection connection,
        IOptions<VisualizerOptions> options,
        ILogger<LiveVesselPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.connection = connection;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation(
            "Enriching {MessageSubject} into {VesselSubject}",
            this.options.MessageSubject,
            this.options.VesselSubject);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (NatsMsg<AisMessageBase> message in this.connection.SubscribeAsync(
                    this.options.MessageSubject,
                    serializer: AisMessageNatsSerializer.Default,
                    cancellationToken: stoppingToken).ConfigureAwait(false))
                {
                    if (message.Data is { } data)
                    {
                        await this.HandleAsync(data, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The broker may not be up yet, or may have gone away. Neither is fatal to a demo that
                // is expected to be started and stopped repeatedly, so back off and resubscribe.
                this.logger.LogWarning(ex, "AIS subscription failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleAsync(AisMessageBase message, CancellationToken cancellationToken)
    {
        // Static messages carry identity. Record it so the next position report can be published with
        // a name and the right colour.
        if (message is IVesselName named)
        {
            string name = named.VesselName.CleanVesselName();
            if (!string.IsNullOrWhiteSpace(name))
            {
                this.identities.AddOrUpdate(
                    message.Mmsi,
                    _ => new VesselIdentity(name, string.Empty, string.Empty, UnknownColor),
                    (_, existing) => existing with { Name = name });
            }
        }

        if (message is IShipType shipType)
        {
            (string category, int[] color) = ShipTypeColors.GetCategoryAndColor(shipType.ShipType);

            this.identities.AddOrUpdate(
                message.Mmsi,
                _ => new VesselIdentity(string.Empty, shipType.ShipType.ToString(), category, color),
                (_, existing) => existing with
                {
                    ShipType = shipType.ShipType.ToString(),
                    ShipTypeCategory = category,
                    Color = color,
                });
        }

        if (message is not IVesselNavigation { Position: { } position } navigation)
        {
            return;
        }

        // The same guard the replay pipeline applies: a feed reports 0,0 and out-of-range values for
        // "unknown", which would otherwise draw vessels off the coast of Africa.
        if (position.Latitude is 0 or > 90 or < -90 || position.Longitude is 0 or > 180 or < -180)
        {
            return;
        }

        VesselIdentity identity = this.identities.GetValueOrDefault(
            message.Mmsi,
            new VesselIdentity(string.Empty, string.Empty, string.Empty, UnknownColor));

        VesselPositionUpdate update = new(
            message.Mmsi,
            string.IsNullOrEmpty(identity.Name) ? $"MMSI {message.Mmsi}" : identity.Name,
            identity.ShipType,
            identity.ShipTypeCategory,
            identity.Color,
            [position.Longitude, position.Latitude],
            navigation.SpeedOverGround ?? 0,
            navigation.CourseOverGround ?? 0,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await this.connection.PublishAsync(
            this.options.VesselSubject,
            JsonSerializer.Serialize(update, SerializerOptions),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct VesselIdentity(string Name, string ShipType, string ShipTypeCategory, int[] Color);
}
