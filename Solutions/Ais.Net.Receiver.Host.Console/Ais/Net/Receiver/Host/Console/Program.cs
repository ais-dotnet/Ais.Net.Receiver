// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Configuration;
using Ais.Net.Receiver.Host.Console.Commands;
using Ais.Net.Receiver.Host.Console.Infrastructure;
using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console.Cli;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults("Ais.Net.Receiver.Console", "Ais.Net.Receiver");

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

builder.Services.AddSingleton(TimeProvider.System);

TypeRegistrar registrar = new(builder.Services);

// This app drives Spectre.Console.Cli rather than building/running an IHost, so the
// ValidateOnStart() calls above never fire on their own. Run the registered startup
// validators explicitly to fail fast on invalid configuration before the command executes.
using (ServiceProvider validationProvider = builder.Services.BuildServiceProvider())
{
    validationProvider.GetService<IStartupValidator>()?.Validate();
}

CommandApp<ReceiveCommand> app = new(registrar);

app.Configure(config => config.SetApplicationName("Ais.Net.Receiver.Host.Console"));

return await app.RunAsync(args);