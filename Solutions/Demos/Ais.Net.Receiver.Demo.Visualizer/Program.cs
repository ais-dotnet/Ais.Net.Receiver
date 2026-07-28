// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Demo.Tracks.Models;
using Ais.Net.Receiver.Demo.Tracks.Output;
using Ais.Net.Receiver.Demo.Visualizer;
using Ais.Net.Receiver.Demo.Visualizer.Live;
using Ais.Net.Receiver.Demo.Visualizer.Replay;

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

        if (string.IsNullOrWhiteSpace(options.Replay.ContainerName))
        {
            options.Replay.ContainerName = builder.Configuration["Storage:ContainerName"] ?? "nmea-ais-dev";
        }
    });

builder.Services.AddSingleton<ReplayTrackSource>();

// Live mode needs the broker; replay does not, so a replay-only run works with no NATS at all.
bool live = builder.Configuration.GetValue($"{VisualizerOptions.SectionName}:Source", VisualizerSource.Live)
    == VisualizerSource.Live;

if (live && !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("nats")))
{
    builder.AddNatsClient("nats");
    builder.Services.AddHostedService<LiveVesselPublisher>();
}

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
        nats = value.Source == VisualizerSource.Live
            ? new
            {
                // Aspire describes the endpoint as HTTP, because that is what it is until the upgrade
                // handshake; the browser client needs it addressed as a websocket.
                url = ToWebSocketUrl(value.NatsWebSocketUrl),
                subject = value.VesselSubject,

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

static string? ToWebSocketUrl(string? url) => url switch
{
    null or "" => url,
    _ when url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) => "wss://" + url[8..],
    _ when url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) => "ws://" + url[7..],
    _ => url,
};

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
