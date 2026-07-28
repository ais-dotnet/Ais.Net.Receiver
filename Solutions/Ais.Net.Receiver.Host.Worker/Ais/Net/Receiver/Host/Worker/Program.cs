// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Host.Worker;
using Ais.Net.Receiver.Host.Worker.Publishing;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Microsoft.Extensions.Options;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults("Ais.Net.Receiver");
builder.Services.AddSystemd();

// Configure options with validation
builder.Services.AddOptions<AisConfig>()
    .Bind(builder.Configuration.GetSection("Ais"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<StorageConfig>()
    .Bind(builder.Configuration.GetSection("Storage"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ConnectionString/ContainerName are only required when capture is enabled.
builder.Services.AddSingleton<IValidateOptions<StorageConfig>, StorageConfigValidator>();

// Configure host options for graceful shutdown and exception behavior
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;

    // Give the host enough time to allow the Worker's 30s flush to complete
    options.ShutdownTimeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddSingleton(TimeProvider.System);

// Publishing decoded messages to NATS is opt-in by configuration: the AppHost supplies a connection
// string so the visualiser demo can subscribe, while a standalone worker under Docker or systemd has
// none and keeps the no-op publisher. Presence of the connection string is the switch, matching how
// storage capture is turned on.
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("nats")))
{
    builder.AddNatsClient("nats");
    builder.Services.AddSingleton<NatsAisMessagePublisher>();
    builder.Services.AddSingleton<IAisMessagePublisher>(sp => sp.GetRequiredService<NatsAisMessagePublisher>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsAisMessagePublisher>());
}
else
{
    builder.Services.AddSingleton<IAisMessagePublisher, NullAisMessagePublisher>();
}

builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
await host.RunAsync();