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

// builder.AddProject<Projects.Ais_Net_Receiver_Host_Console>("console");
builder.AddProject<Projects.Ais_Net_Receiver_Host_Worker>("worker")

    // The receiver reads its own Storage section rather than a ConnectionStrings entry, so the
    // emulator's connection string is mapped onto that key instead of using WithReference. Capture is
    // off in appsettings.json (it needs a connection string to go with it); with one supplied here,
    // the local run turns it on.
    .WithEnvironment("Storage__EnableCapture", "true")
    .WithEnvironment("Storage__ConnectionString", blobs)
    .WaitFor(blobs);

builder.Build().Run();
