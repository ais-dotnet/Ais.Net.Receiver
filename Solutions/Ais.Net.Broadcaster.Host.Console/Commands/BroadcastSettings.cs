// <copyright file="BroadcastSettings.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel;
using System.Globalization;
using System.Net;

using Microsoft.Extensions.Logging;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Ais.Net.Broadcaster.Host.Console.Commands;

/// <summary>
/// The sink, pacing, fault injection and output settings shared by <c>replay</c> and <c>relay</c>.
/// </summary>
public abstract class BroadcastSettings : CommandSettings
{
    [CommandOption("--tcp <PORT>")]
    [Description("Serve newline-terminated sentences on a raw TCP socket at this port (0 picks a free port).")]
    public int? TcpPort { get; init; }

    [CommandOption("--ws <PORT>")]
    [Description("Serve sentences as WebSocket text messages at this port (0 picks a free port).")]
    public int? WebSocketPort { get; init; }

    [CommandOption("--bind <ADDRESS>")]
    [Description("The address the listeners bind to.")]
    [DefaultValue("0.0.0.0")]
    public string Bind { get; init; } = "0.0.0.0";

    [CommandOption("--ws-path <PATH>")]
    [Description("The request path WebSocket clients connect to.")]
    [DefaultValue("/")]
    public string WebSocketPath { get; init; } = "/";

    [CommandOption("--ws-framing <FRAMING>")]
    [Description("[blue]message[/]: one sentence per WebSocket message; [blue]batch[/]: several newline-terminated sentences per message.")]
    [DefaultValue("message")]
    public string Framing { get; init; } = "message";

    [CommandOption("--ws-batch-size <N>")]
    [Description("With --ws-framing batch, the most sentences per message. Defaults to 50.")]
    public int? WebSocketBatchSize { get; init; }

    [CommandOption("--ws-batch-interval <MS>")]
    [Description("With --ws-framing batch, the longest a partial batch waits, in milliseconds. Defaults to 200.")]
    public int? WebSocketBatchIntervalMs { get; init; }

    [CommandOption("--line-ending <ENDING>")]
    [Description("The terminator after each sentence: [blue]lf[/] or [blue]crlf[/].")]
    [DefaultValue("crlf")]
    public string LineEnding { get; init; } = "crlf";

    [CommandOption("--queue <N>")]
    [Description("Sentences queued per client before --slow-client applies. Defaults to 4096.")]
    public int? QueueCapacity { get; init; }

    [CommandOption("--slow-client <POLICY>")]
    [Description("What happens to a client whose queue is full: [blue]drop[/] its oldest sentences, or [blue]disconnect[/] it. Defaults to drop.")]
    public string? SlowClient { get; init; }

    [CommandOption("--pace <MODE>")]
    [Description("[blue]none[/]: as fast as the source delivers; [blue]fixed[/]: --rate sentences per second; [blue]tags[/]: reproduce the tag-block c: timestamps. Defaults to tags for replay and none for relay.")]
    public string? Pace { get; init; }

    [CommandOption("--rate <PER-SECOND>")]
    [Description("With --pace fixed, sentences per second. Defaults to 100.")]
    public double? Rate { get; init; }

    [CommandOption("--speed <FACTOR>")]
    [Description("With --pace tags, the replay speed; 2 is twice as fast. Defaults to 1.")]
    public double? Speed { get; init; }

    [CommandOption("--max-gap <SECONDS>")]
    [Description("With --pace tags, the longest gap honoured between timestamps. Defaults to 5.")]
    public double? MaxGap { get; init; }

    [CommandOption("--disconnect-every <SECONDS>")]
    [Description("Fault: drop every connected client this often, so receivers must reconnect.")]
    public double? DisconnectEvery { get; init; }

    [CommandOption("--silence-every <SECONDS>")]
    [Description("Fault: go silent this often while keeping clients connected, so idle timeouts fire.")]
    public double? SilenceEvery { get; init; }

    [CommandOption("--silence-for <SECONDS>")]
    [Description("Fault: how long each silence lasts. Defaults to 90.")]
    public double? SilenceFor { get; init; }

    [CommandOption("--malformed-every <N>")]
    [Description("Fault: send a malformed line in place of every Nth sentence.")]
    public int? MalformedEvery { get; init; }

    [CommandOption("--overlong-every <N>")]
    [Description("Fault: send a line over the receivers' 8 KiB cap in place of every Nth sentence.")]
    public int? OverLongEvery { get; init; }

    [CommandOption("-q|--quiet")]
    [Description("Show no live statistics; print nothing but errors.")]
    public bool Quiet { get; init; }

    [CommandOption("--verbosity <LEVEL>")]
    [Description("The log level: trace, debug, information, warning, error, critical or none. Defaults to warning (error with --quiet).")]
    public string? Verbosity { get; init; }

    /// <summary>Gets the pacing used when <c>--pace</c> is not given.</summary>
    protected abstract PacingMode DefaultPacing { get; }

    public override ValidationResult Validate()
    {
        string? error = this.ValidateCore();
        return error is null ? ValidationResult.Success() : ValidationResult.Error(error);
    }

    /// <summary>Gets the pipeline settings these options describe.</summary>
    /// <param name="loop">Whether a finite source restarts when it ends.</param>
    /// <returns>The pipeline options.</returns>
    public BroadcastPipelineOptions CreatePipelineOptions(bool loop)
    {
        BroadcastPipelineOptions defaults = new();
        FaultInjectionOptions faultDefaults = new();

        return new BroadcastPipelineOptions
        {
            Pacing = this.ResolvePacing(),
            SentencesPerSecond = this.Rate ?? defaults.SentencesPerSecond,
            SpeedFactor = this.Speed ?? defaults.SpeedFactor,
            MaxTagBlockGap = this.MaxGap is { } maxGap ? TimeSpan.FromSeconds(maxGap) : defaults.MaxTagBlockGap,
            Loop = loop,
            Faults = new FaultInjectionOptions
            {
                DisconnectEvery = this.DisconnectEvery is { } disconnectEvery ? TimeSpan.FromSeconds(disconnectEvery) : null,
                SilenceEvery = this.SilenceEvery is { } silenceEvery ? TimeSpan.FromSeconds(silenceEvery) : null,
                SilenceDuration = this.SilenceFor is { } silenceFor ? TimeSpan.FromSeconds(silenceFor) : faultDefaults.SilenceDuration,
                MalformedEveryN = this.MalformedEvery,
                OverLongEveryN = this.OverLongEvery,
            },
        };
    }

    /// <summary>Gets the TCP listener settings, or null when <c>--tcp</c> was not given.</summary>
    /// <returns>The TCP broadcaster options, if any.</returns>
    public TcpBroadcasterOptions? CreateTcpOptions()
        => this.TcpPort is { } port ? this.ApplyCommon(new TcpBroadcasterOptions { Port = port }) : null;

    /// <summary>Gets the WebSocket listener settings, or null when <c>--ws</c> was not given.</summary>
    /// <returns>The WebSocket broadcaster options, if any.</returns>
    public WebSocketBroadcasterOptions? CreateWebSocketOptions()
    {
        if (this.WebSocketPort is not { } port)
        {
            return null;
        }

        TryParseFraming(this.Framing, out WebSocketFraming framing);
        WebSocketBroadcasterOptions defaults = new();

        return this.ApplyCommon(new WebSocketBroadcasterOptions
        {
            Port = port,
            Path = this.WebSocketPath,
            Framing = framing,
            BatchSize = this.WebSocketBatchSize ?? defaults.BatchSize,
            BatchInterval = this.WebSocketBatchIntervalMs is { } ms ? TimeSpan.FromMilliseconds(ms) : defaults.BatchInterval,
        });
    }

    /// <summary>Gets the minimum log level: <c>--verbosity</c>, else warning, or error when quiet.</summary>
    /// <returns>The log level.</returns>
    public LogLevel ResolveLogLevel()
    {
        if (this.Verbosity is not null && TryParseLogLevel(this.Verbosity, out LogLevel level))
        {
            return level;
        }

        return this.Quiet ? LogLevel.Error : LogLevel.Warning;
    }

    /// <summary>Gets the pacing mode: <c>--pace</c>, else the command's default.</summary>
    /// <returns>The pacing mode.</returns>
    public PacingMode ResolvePacing()
        => this.Pace is not null && TryParsePacing(this.Pace, out PacingMode pacing) ? pacing : this.DefaultPacing;

    /// <summary>
    /// Checks the shared options. A derived command calls this before its own checks.
    /// </summary>
    /// <returns>A message describing the first problem found, or null if there is none.</returns>
    protected virtual string? ValidateCore()
    {
        if (this.TcpPort is null && this.WebSocketPort is null)
        {
            return "Give at least one listener: --tcp <port>, --ws <port>, or both.";
        }

        if (this.TcpPort is { } tcp && !IsPort(tcp))
        {
            return $"--tcp must be a port from 0 to 65535, not {tcp}.";
        }

        if (this.WebSocketPort is { } ws && !IsPort(ws))
        {
            return $"--ws must be a port from 0 to 65535, not {ws}.";
        }

        if (this.TcpPort is { } shared && shared != 0 && shared == this.WebSocketPort)
        {
            return $"--tcp and --ws cannot share port {shared}.";
        }

        if (!IPAddress.TryParse(this.Bind, out _))
        {
            return $"--bind must be an IP address such as 0.0.0.0, 127.0.0.1 or ::, not '{this.Bind}'.";
        }

        if (string.IsNullOrWhiteSpace(this.WebSocketPath))
        {
            return "--ws-path must not be empty; use / for the root.";
        }

        if (!TryParseFraming(this.Framing, out _))
        {
            return $"--ws-framing must be 'message' or 'batch', not '{this.Framing}'.";
        }

        if (this.WebSocketBatchSize is < 1)
        {
            return "--ws-batch-size must be at least 1.";
        }

        if (this.WebSocketBatchIntervalMs is < 1)
        {
            return "--ws-batch-interval must be at least 1 millisecond.";
        }

        if (!TryParseLineEnding(this.LineEnding, out _))
        {
            return $"--line-ending must be 'lf' or 'crlf', not '{this.LineEnding}'.";
        }

        if (this.QueueCapacity is < 1)
        {
            return "--queue must be at least 1.";
        }

        if (this.SlowClient is not null && !TryParseSlowClient(this.SlowClient, out _))
        {
            return $"--slow-client must be 'drop' or 'disconnect', not '{this.SlowClient}'.";
        }

        if (this.Pace is not null && !TryParsePacing(this.Pace, out _))
        {
            return $"--pace must be 'none', 'fixed' or 'tags', not '{this.Pace}'.";
        }

        PacingMode pacing = this.ResolvePacing();

        if (this.Rate is { } rate)
        {
            if (pacing != PacingMode.Fixed)
            {
                return "--rate applies only with --pace fixed.";
            }

            if (!(rate > 0 && double.IsFinite(rate)))
            {
                return "--rate must be a positive number of sentences per second.";
            }
        }

        if (this.Speed is { } speed)
        {
            if (pacing != PacingMode.TagBlockTimestamps)
            {
                return "--speed applies only with --pace tags.";
            }

            if (!(speed > 0 && double.IsFinite(speed)))
            {
                return "--speed must be a positive factor, such as 0.5 or 10.";
            }
        }

        if (this.MaxGap is { } maxGap)
        {
            if (pacing != PacingMode.TagBlockTimestamps)
            {
                return "--max-gap applies only with --pace tags.";
            }

            if (!(maxGap >= 0 && double.IsFinite(maxGap)))
            {
                return "--max-gap must be zero or a positive number of seconds.";
            }
        }

        if (this.DisconnectEvery is { } disconnectEvery && !IsPositiveSeconds(disconnectEvery))
        {
            return "--disconnect-every must be a positive number of seconds.";
        }

        if (this.SilenceEvery is { } silenceEvery && !IsPositiveSeconds(silenceEvery))
        {
            return "--silence-every must be a positive number of seconds.";
        }

        if (this.SilenceFor is { } silenceFor)
        {
            if (this.SilenceEvery is null)
            {
                return "--silence-for needs --silence-every to say how often the silences happen.";
            }

            if (!(silenceFor >= 0 && double.IsFinite(silenceFor)))
            {
                return "--silence-for must be zero or a positive number of seconds.";
            }
        }

        if (this.MalformedEvery is < 1)
        {
            return "--malformed-every must be at least 1.";
        }

        if (this.OverLongEvery is < 1)
        {
            return "--overlong-every must be at least 1.";
        }

        if (this.Verbosity is not null && !TryParseLogLevel(this.Verbosity, out _))
        {
            return $"--verbosity must be one of trace, debug, information, warning, error, critical or none, not '{this.Verbosity}'.";
        }

        return null;
    }

    private static bool IsPort(int port) => port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;

    private static bool IsPositiveSeconds(double seconds) => seconds > 0 && double.IsFinite(seconds);

    private static bool TryParseFraming(string value, out WebSocketFraming framing)
    {
        (bool parsed, framing) = value.ToLowerInvariant() switch
        {
            "message" => (true, WebSocketFraming.SentencePerMessage),
            "batch" => (true, WebSocketFraming.NewlineDelimitedBatch),
            _ => (false, default(WebSocketFraming)),
        };

        return parsed;
    }

    private static bool TryParseLineEnding(string value, out NmeaLineEnding lineEnding)
    {
        (bool parsed, lineEnding) = value.ToLowerInvariant() switch
        {
            "lf" => (true, NmeaLineEnding.Lf),
            "crlf" => (true, NmeaLineEnding.CrLf),
            _ => (false, default(NmeaLineEnding)),
        };

        return parsed;
    }

    private static bool TryParseSlowClient(string value, out SlowClientPolicy policy)
    {
        (bool parsed, policy) = value.ToLowerInvariant() switch
        {
            "drop" => (true, SlowClientPolicy.DropOldest),
            "disconnect" => (true, SlowClientPolicy.Disconnect),
            _ => (false, default(SlowClientPolicy)),
        };

        return parsed;
    }

    private static bool TryParsePacing(string value, out PacingMode pacing)
    {
        (bool parsed, pacing) = value.ToLowerInvariant() switch
        {
            "none" => (true, PacingMode.None),
            "fixed" => (true, PacingMode.Fixed),
            "tags" => (true, PacingMode.TagBlockTimestamps),
            _ => (false, default(PacingMode)),
        };

        return parsed;
    }

    private static bool TryParseLogLevel(string value, out LogLevel level)
    {
        switch (value.ToLowerInvariant())
        {
            case "info":
                level = LogLevel.Information;
                return true;

            case "warn":
                level = LogLevel.Warning;
                return true;
        }

        // Enum.TryParse also accepts numbers, which are not a documented spelling.
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
            Enum.TryParse(value, ignoreCase: true, out level))
        {
            return true;
        }

        level = default;
        return false;
    }

    private T ApplyCommon<T>(T options)
        where T : BroadcasterOptions
    {
        TryParseLineEnding(this.LineEnding, out NmeaLineEnding lineEnding);
        SlowClientPolicy slowClient = this.SlowClient is not null && TryParseSlowClient(this.SlowClient, out SlowClientPolicy policy)
            ? policy
            : options.SlowClient;

        return options with
        {
            Address = IPAddress.Parse(this.Bind),
            LineEnding = lineEnding,
            ClientQueueCapacity = this.QueueCapacity ?? options.ClientQueueCapacity,
            SlowClient = slowClient,
        };
    }
}
