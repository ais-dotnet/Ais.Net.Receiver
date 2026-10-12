// <copyright file="ReplayCommand.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel;

using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Logging;

using Spectre.Console.Cli;
using Spectre.IO;

namespace Ais.Net.Broadcaster.Host.Console.Commands;

/// <summary>
/// <c>ais-broadcaster replay &lt;file&gt;</c>: broadcasts a recorded NMEA file, by default paced by
/// its tag-block timestamps and looped forever.
/// </summary>
public sealed class ReplayCommand : AsyncCommand<ReplayCommand.Settings>
{
    private readonly BroadcastRunner runner;
    private readonly ILoggerFactory loggerFactory;
    private readonly TimeProvider timeProvider;

    public ReplayCommand(BroadcastRunner runner, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        this.runner = runner;
        this.loggerFactory = loggerFactory;
        this.timeProvider = timeProvider;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string path = System.IO.Path.GetFullPath(settings.File);

        FileNmeaConnection connection = new(
            new FileSystem(),
            new FilePath(path),
            logger: this.loggerFactory.CreateLogger<FileNmeaConnection>());

        NmeaReceiver source = new(
            connection,
            this.timeProvider,
            logger: this.loggerFactory.CreateLogger<NmeaReceiver>());

        return this.runner.RunAsync(source, path, settings, settings.ResolveLoop(), cancellationToken);
    }

    public sealed class Settings : BroadcastSettings
    {
        [CommandArgument(0, "<FILE>")]
        [Description("The recording to replay: one NMEA sentence per line, optionally with NMEA 4.10 tag blocks.")]
        public string File { get; init; } = string.Empty;

        [CommandOption("--loop [ENABLED]")]
        [Description("Restart from the beginning when the file ends. On by default; --loop false or --no-loop replays once and exits.")]
        public FlagValue<bool>? Loop { get; init; }

        [CommandOption("--no-loop")]
        [Description("Replay the file once and exit.")]
        public bool NoLoop { get; init; }

        protected override PacingMode DefaultPacing => PacingMode.TagBlockTimestamps;

        /// <summary>Gets whether the replay loops: on unless <c>--no-loop</c> or <c>--loop false</c>.</summary>
        /// <returns><see langword="true"/> to loop.</returns>
        public bool ResolveLoop()
        {
            if (this.NoLoop)
            {
                return false;
            }

            // A bare --loop sets the flag with no value, which means true.
            return this.Loop is not { IsSet: true } loop || loop.Value;
        }

        protected override string? ValidateCore()
        {
            if (string.IsNullOrWhiteSpace(this.File))
            {
                return "Give the file to replay, for example: ais-broadcaster replay samples/sample.nm4 --tcp 5631";
            }

            if (!System.IO.File.Exists(this.File))
            {
                return System.IO.Directory.Exists(this.File)
                    ? $"'{this.File}' is a directory; give the path of a recording file."
                    : $"File not found: '{System.IO.Path.GetFullPath(this.File)}'.";
            }

            if (this.NoLoop && this.Loop is { IsSet: true, Value: true })
            {
                return "--loop and --no-loop cannot be used together.";
            }

            return base.ValidateCore();
        }
    }
}
