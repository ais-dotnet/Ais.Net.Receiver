// <copyright file="Program.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Broadcaster.Host.Console;
using Ais.Net.Broadcaster.Host.Console.Commands;
using Ais.Net.Broadcaster.Host.Console.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using Spectre.Console.Cli;

// An empty builder: no appsettings, no environment-variable configuration. Everything this tool
// does is driven by its command line, and the container exists for logging and the commands.
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

// --verbosity is parsed by Spectre.Console.Cli after the container is built, so the console
// provider lets everything through and a switch the command sets does the filtering. Logs go to
// stderr so they never tear the live statistics table on stdout.
LogLevelSwitch logLevel = new();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddFilter((_, level) => logLevel.IsEnabled(level));
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(logLevel);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BroadcastRunner>();

// Ctrl+C and SIGTERM (which raises ProcessExit) both stop the pipeline gracefully: the sinks close
// their clients and the process exits 0. A second Ctrl+C terminates immediately.
CancellationTokenSource shutdown = new();
ManualResetEventSlim finished = new();

System.Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = !shutdown.IsCancellationRequested;
    shutdown.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    shutdown.Cancel();

    // Hold the process open until the run has wound down, but not indefinitely.
    finished.Wait(TimeSpan.FromSeconds(10));
};

CommandApp app = new(new TypeRegistrar(builder.Services));

app.Configure(config =>
{
    config.SetApplicationName("ais-broadcaster");

    // Cancellation is how a broadcast normally ends.
    config.Settings.CancellationExitCode = 0;

    config.SetExceptionHandler((ex, _) =>
    {
        System.Console.Error.WriteLine($"ais-broadcaster: {ex.Message}");
        return 1;
    });

    config.AddCommand<ReplayCommand>("replay")
        .WithDescription("Broadcast a recorded NMEA file, paced by its tag-block timestamps and looped by default.")
        .WithExample("replay", "samples/sample.nm4", "--tcp", "5631", "--ws", "8080")
        .WithExample("replay", "recording.nm4", "--ws", "8080", "--pace", "fixed", "--rate", "500", "--no-loop");

    config.AddCommand<RelayCommand>("relay")
        .WithDescription("Re-broadcast a live TCP or WebSocket feed to local TCP and WebSocket clients.")
        .WithExample("relay", "tcp://153.44.253.27:5631", "--ws", "8080")
        .WithExample("relay", "tcp://153.44.253.27:5631", "--tcp", "5631", "--disconnect-every", "60", "--malformed-every", "100");
});

try
{
    return await app.RunAsync(args, shutdown.Token);
}
finally
{
    finished.Set();
}
