using Aspire.Hosting.Azure;

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
// what makes that reach the browser directly: the visualiser page subscribes with nats.ws rather than
// the ASP.NET Core host relaying the stream. nats-server has no command-line switch for websockets -
// it is configuration-file only - hence the bind-mounted nats.conf.
IResourceBuilder<NatsServerResource> nats = builder.AddNats("nats")
    .WithBindMount("nats/nats.conf", "/etc/nats/nats.conf", isReadOnly: true)
    .WithArgs("-c", "/etc/nats/nats.conf")

    // Declared with the ws scheme so GetEndpoint("ws") yields a browser-ready ws:// URL - the
    // visualiser hands it to the page verbatim, with no scheme rewriting anywhere.
    .WithEndpoint(targetPort: 8080, scheme: "ws", name: "ws");

// builder.AddProject<Projects.Ais_Net_Receiver_Host_Console>("console");
builder.AddProject<Projects.Ais_Net_Receiver_Host_Worker>("worker")
    .WithReference(nats)
    .WaitFor(nats)

    // The receiver reads its own Storage section rather than a ConnectionStrings entry, so the
    // emulator's connection string is mapped onto that key instead of using WithReference. Capture is
    // off in appsettings.json (it needs a connection string to go with it); with one supplied here,
    // the local run turns it on.
    .WithEnvironment("Storage__EnableCapture", "true")
    .WithEnvironment("Storage__ConnectionString", blobs)
    .WaitFor(blobs);

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
