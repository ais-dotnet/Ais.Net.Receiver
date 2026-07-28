// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks.Models;
using Ais.Net.Receiver.Demo.Tracks.Output;
using Ais.Net.Receiver.Demo.Tracks.Processing;
using Ais.Net.Receiver.Demo.Visualizer;
using Ais.Net.Receiver.Demo.Visualizer.Replay;
using Ais.Net.Receiver.Hosting;

using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// No AIS health checks: this host renders what the receiver publishes, it does not run one, so the
// connection check would report the visualiser unhealthy for its entire life.
builder.AddServiceDefaults(
    "Ais.Net.Receiver.Demo.Visualizer",
    includeAisHealthChecks: false,
    "Ais.Net.Receiver");

builder.Services.AddOptions<VisualizerOptions>()
    .Bind(builder.Configuration.GetSection(VisualizerOptions.SectionName))
    .PostConfigure(options =>
    {
        // Replaying a captured blob should need nothing beyond the blob path: fall back to the
        // receiver's own storage connection string, which under the AppHost already points at Azurite.
        options.Replay.ConnectionString ??= builder.Configuration["Storage:ConnectionString"];
    });

builder.Services.AddSingleton<ReplayTrackSource>();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();

// What the page needs before it can draw anything: which mode to run in, and - for live - where the
// browser's own NATS connection should go. The port is assigned by the AppHost at run time, so this
// cannot be baked into the bundle.
app.MapGet("/api/config", (IOptions<VisualizerOptions> options, IConfiguration configuration) =>
{
    VisualizerOptions value = options.Value;
    NatsCredentials? credentials = NatsCredentials.FromConnectionString(configuration.GetConnectionString("nats"));

    return Results.Ok(new
    {
        source = value.Source.ToString().ToLowerInvariant(),
        basemapStyle = value.BasemapStyle,
        vesselInactivitySeconds = (int)value.VesselInactivityTimeout.TotalSeconds,

        // The page decodes raw AIS messages itself, so it needs the ship-type mapping the replay
        // pipeline uses. Serving it keeps the categories and palette defined once, in C#, instead of
        // a second copy in JavaScript that can drift from the recorded view.
        shipTypeStyles = ShipTypeColors.GetStyleTable(),
        nats = value.Source == VisualizerSource.Live
            ? new
            {
                // Already a ws:// URL: the AppHost declares the endpoint with the ws scheme.
                url = value.NatsWebSocketUrl,
                subject = AisNats.MessagesSubject,

                // Local demo credentials, generated per run by the AppHost. They are handed to the
                // page because the browser connects to the broker itself; that is only acceptable
                // because this listens on loopback with no TLS. Anything shared needs proper auth.
                user = credentials?.User,
                pass = credentials?.Password,
            }
            : null,
    });
});

// Replay data. This is a server endpoint because the source may be an Azure blob, which the browser
// cannot read without a credential, and because parsing a recording is far too slow to repeat per
// request.
app.MapGet("/api/tracks", async (
    ReplayTrackSource source,
    IOptions<VisualizerOptions> options,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (options.Value.Source != VisualizerSource.Replay)
    {
        return Results.BadRequest(new { error = "The visualiser is running in live mode; set Visualizer:Source=Replay." });
    }

    (IReadOnlyList<VesselTrack> tracks, string label) = await source.GetAsync(cancellationToken);

    context.Response.ContentType = "application/json";
    await DeckGlJsonWriter.SerializeAsync(context.Response.Body, label, tracks, cancellationToken);

    return Results.Empty;
});

await app.RunAsync();

/// <summary>
/// The user and password out of a NATS connection string, so the page can authenticate its own
/// websocket connection.
/// </summary>
/// <param name="User">The user name.</param>
/// <param name="Password">The password.</param>
internal sealed record NatsCredentials(string User, string Password)
{
    /// <summary>
    /// Parses credentials from a <c>nats://user:pass@host:port</c> connection string.
    /// </summary>
    /// <param name="connectionString">The connection string, which may be null or credential-free.</param>
    /// <returns>The credentials, or null when the string carries none.</returns>
    public static NatsCredentials? FromConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) ||
            !Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        string[] parts = uri.UserInfo.Split(':', 2);

        return parts.Length == 2 ? new NatsCredentials(parts[0], parts[1]) : null;
    }
}
