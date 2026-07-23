// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Host.Worker;
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
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
await host.RunAsync();