// <copyright file="RelayCommand.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel;

using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Logging;

using Spectre.Console.Cli;

namespace Ais.Net.Broadcaster.Host.Console.Commands;

/// <summary>
/// <c>ais-broadcaster relay &lt;source-url&gt;</c>: re-broadcasts a live TCP or WebSocket feed, so one
/// upstream connection can serve many local receivers.
/// </summary>
public sealed class RelayCommand : AsyncCommand<RelayCommand.Settings>
{
    private readonly BroadcastRunner runner;
    private readonly ILoggerFactory loggerFactory;
    private readonly TimeProvider timeProvider;

    public RelayCommand(BroadcastRunner runner, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        this.runner = runner;
        this.loggerFactory = loggerFactory;
        this.timeProvider = timeProvider;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Uri url = new(settings.SourceUrl, UriKind.Absolute);

        INmeaConnection connection = url.Scheme switch
        {
            "tcp" => new TcpNmeaConnection(url.DnsSafeHost, url.Port, this.loggerFactory.CreateLogger<TcpNmeaConnection>()),
            _ => new WebSocketNmeaConnection(url, this.loggerFactory.CreateLogger<WebSocketNmeaConnection>()),
        };

        NmeaReceiverOptions options = new()
        {
            RetryAttemptLimit = settings.RetryAttempts,
            RetryPeriodicity = TimeSpan.FromSeconds(settings.RetryPeriodicity),
        };

        NmeaReceiver source = new(
            connection,
            this.timeProvider,
            options,
            logger: this.loggerFactory.CreateLogger<NmeaReceiver>());

        return this.runner.RunAsync(source, url.ToString(), settings, loop: false, cancellationToken);
    }

    public sealed class Settings : BroadcastSettings
    {
        [CommandArgument(0, "<SOURCE-URL>")]
        [Description("The live feed to relay: tcp://host:port, ws://host:port/path or wss://host/path.")]
        public string SourceUrl { get; init; } = string.Empty;

        [CommandOption("--retry-attempts <N>")]
        [Description("The reconnect attempt at which the linear backoff stops growing.")]
        [DefaultValue(100)]
        public int RetryAttempts { get; init; } = 100;

        [CommandOption("--retry-periodicity <SECONDS>")]
        [Description("The base reconnect delay, multiplied by the attempt count.")]
        [DefaultValue(1.0)]
        public double RetryPeriodicity { get; init; } = 1;

        protected override PacingMode DefaultPacing => PacingMode.None;

        protected override string? ValidateCore()
        {
            if (!Uri.TryCreate(this.SourceUrl, UriKind.Absolute, out Uri? url))
            {
                return $"'{this.SourceUrl}' is not an absolute URL; use tcp://host:port, ws://host:port/path or wss://host/path.";
            }

            switch (url.Scheme)
            {
                case "tcp":
                    if (url.Port is < 1 or > 65535)
                    {
                        return $"'{this.SourceUrl}' needs a port, for example tcp://153.44.253.27:5631.";
                    }

                    break;

                case "ws":
                case "wss":
                    break;

                default:
                    return $"Unsupported source scheme '{url.Scheme}'; use tcp://, ws:// or wss://.";
            }

            if (this.RetryAttempts < 1)
            {
                return "--retry-attempts must be at least 1.";
            }

            if (!(this.RetryPeriodicity > 0 && double.IsFinite(this.RetryPeriodicity)))
            {
                return "--retry-periodicity must be a positive number of seconds.";
            }

            return base.ValidateCore();
        }
    }
}
