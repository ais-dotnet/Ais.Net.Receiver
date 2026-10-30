using Aspire.Hosting.Azure;

using Microsoft.Extensions.Configuration;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Azurite stands in for Azure Storage locally, so capture runs on a fresh clone with no account and
// no secrets. The blob endpoint is handed to the worker below; nothing here reaches Azure.
IResourceBuilder<AzureBlobStorageResource> blobs = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator
        // Keep captured NMEA across restarts, so a rotation or replay can be inspected after the
        // fact rather than vanishing with the container.
        .WithDataVolume("ais-azurite-data")

        // Azurite trails the newest x-ms-version the Azure SDK sends and rejects an otherwise valid
        // request with 400 InvalidHeaderValue when it does. The integration suite skips the check for
        // the same reason; without this, writes fail whenever the SDK is ahead of the emulator.
        .WithArgs("--skipApiVersionCheck"))
    .AddBlobs("blobs");

// NATS carries decoded vessel positions from the worker to the visualiser. The websocket listener is
// what makes that reach the browser directly: the visualiser page subscribes with @nats-io/nats-core rather than
// the ASP.NET Core host relaying the stream. nats-server has no command-line switch for websockets -
// it is configuration-file only - hence the bind-mounted nats.conf.
IResourceBuilder<NatsServerResource> nats = builder.AddNats("nats")
    .WithBindMount("nats/nats.conf", "/etc/nats/nats.conf", isReadOnly: true)
    .WithArgs("-c", "/etc/nats/nats.conf")

    // Declared with the ws scheme so GetEndpoint("ws") yields a browser-ready ws:// URL - the
    // visualiser hands it to the page verbatim, with no scheme rewriting anywhere.
    .WithEndpoint(targetPort: 8080, scheme: "ws", name: "ws");

// The broadcaster replays a recording that ships with it, looped and paced by its tag-block
// timestamps, so there is always a feed to point the worker at - one that needs no internet connection
// and does not depend on the Norwegian Coastal Administration's server being up. It prefers the real
// capture (about a minute of the Norwegian feed, NLOD-licensed) when that file is present, and falls
// back to the small synthetic sample otherwise. Its ports are fixed on the command line and declared
// as target ports, so Aspire proxies them like any other endpoint.
string broadcasterSamples = Path.Combine(builder.AppHostDirectory, "..", "Ais.Net.Broadcaster.Host.Console", "samples");
string recording = File.Exists(Path.Combine(broadcasterSamples, "kystverket-20260511T00.nm4"))
    ? "samples/kystverket-20260511T00.nm4"
    : "samples/sample.nm4";

// A longer local recording can replace the sample without touching the repo: set AisRecording to its
// absolute path, typically with dotnet user-secrets. The AppHost does not run under the Development
// environment that would load those by default, so they are added here. Real captures interleave stale
// timestamps, each of which would stall the replay for the default 5 second maximum gap, so these get a
// shorter one.
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
List<string> broadcasterArgs = ["replay", recording, "--tcp", "15631", "--ws", "18080", "--pace", "tags", "--quiet"];
if (builder.Configuration["AisRecording"] is { Length: > 0 } localRecording)
{
    if (!File.Exists(localRecording))
    {
        throw new FileNotFoundException($"AisRecording points at a file that does not exist: '{localRecording}'.");
    }

    broadcasterArgs[1] = localRecording;
    broadcasterArgs.AddRange(["--max-gap", "1"]);
}

IResourceBuilder<ProjectResource> broadcaster = builder.AddProject<Projects.Ais_Net_Broadcaster_Host_Console>("broadcaster")
    .WithArgs([.. broadcasterArgs])
    .WithEndpoint(targetPort: 15631, scheme: "tcp", name: "tcp")

    // Declared with the ws scheme so GetEndpoint("ws") yields a ws:// URL the worker can use verbatim.
    .WithEndpoint(targetPort: 18080, scheme: "ws", name: "ws");

// Which feed the worker reads. The default is the live Kystverket feed from appsettings.json; set
// AisSource to "broadcaster" to read the local replay instead - as an environment variable
// (AisSource=broadcaster), or on the command line (dotnet run -- --AisSource broadcaster), since the
// builder's configuration takes both.
string aisSource = builder.Configuration["AisSource"] ?? "kystverket";

// builder.AddProject<Projects.Ais_Net_Receiver_Host_Console>("console");
IResourceBuilder<ProjectResource> worker = builder.AddProject<Projects.Ais_Net_Receiver_Host_Worker>("worker")
    .WithReference(nats)
    .WaitFor(nats)

    // The receiver reads its own Storage section rather than a ConnectionStrings entry, so the
    // emulator's connection string is mapped onto that key instead of using WithReference. Capture is
    // off in appsettings.json (it needs a connection string to go with it); with one supplied here,
    // the local run turns it on.
    .WithEnvironment("Storage__EnableCapture", "true")
    .WithEnvironment("Storage__ConnectionString", blobs)
    .WaitFor(blobs);

if (string.Equals(aisSource, "broadcaster", StringComparison.OrdinalIgnoreCase))
{
    // The broadcaster serves the same NMEA the live feed does, so only the connection changes: the
    // WebSocket transport, at whatever host port Aspire allocated for the broadcaster's ws endpoint.
    worker
        .WithEnvironment("Ais__Connection__Transport", "WebSocket")
        .WithEnvironment("Ais__Connection__Url", broadcaster.GetEndpoint("ws"))
        .WaitFor(broadcaster);
}

// The AIS Visualizer demo. It serves the deck.gl page; the page itself subscribes to the messages the
// worker publishes, so this host relays no vessel data.
builder.AddProject<Projects.Ais_Net_Receiver_Demo_Visualizer>("visualizer")

    // Referenced for the connection string alone - the credentials in it are handed to the page so it
    // can open its own connection. Waiting for the broker keeps the first page load from racing it.
    .WithReference(nats)
    .WaitFor(nats)

    // The browser opens that connection, so it needs the websocket endpoint as seen from the host
    // rather than from inside the container network.
    .WithEnvironment("Visualizer__NatsWebSocketUrl", nats.GetEndpoint("ws"))

    // Lets a replay read the hourly blobs the worker captures, without any extra configuration.
    .WithEnvironment("Storage__ConnectionString", blobs)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
