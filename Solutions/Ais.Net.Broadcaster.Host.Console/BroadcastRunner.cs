// <copyright file="BroadcastRunner.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Globalization;

using Ais.Net.Broadcaster.Host.Console.Commands;
using Ais.Net.Broadcaster.Host.Console.Infrastructure;
using Ais.Net.Receiver.Receiver;

using Microsoft.Extensions.Logging;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace Ais.Net.Broadcaster.Host.Console;

/// <summary>
/// The part of <c>replay</c> and <c>relay</c> that is the same once the source exists: build the
/// sinks and the pipeline, run until the source ends or the user cancels, and show live statistics.
/// </summary>
public sealed class BroadcastRunner
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StatisticsLineInterval = TimeSpan.FromSeconds(10);

    private readonly ILoggerFactory loggerFactory;
    private readonly TimeProvider timeProvider;
    private readonly LogLevelSwitch logLevel;

    public BroadcastRunner(ILoggerFactory loggerFactory, TimeProvider timeProvider, LogLevelSwitch logLevel)
    {
        this.loggerFactory = loggerFactory;
        this.timeProvider = timeProvider;
        this.logLevel = logLevel;
    }

    /// <summary>
    /// Broadcasts <paramref name="source"/> to the listeners <paramref name="settings"/> describe.
    /// Takes ownership of the source and disposes it.
    /// </summary>
    /// <param name="source">The sentences to broadcast.</param>
    /// <param name="sourceDescription">The file path or URL, for the live display.</param>
    /// <param name="settings">The listener, pacing, fault and output settings.</param>
    /// <param name="loop">Whether a finite source restarts when it ends.</param>
    /// <param name="cancellationToken">Cancelled by Ctrl+C or process shutdown.</param>
    /// <returns>0 when the source ended or the run was cancelled; 1 when it failed.</returns>
    public async Task<int> RunAsync(INmeaReceiver source, string sourceDescription, BroadcastSettings settings, bool loop, CancellationToken cancellationToken)
    {
        this.logLevel.MinimumLevel = settings.ResolveLogLevel();

        List<INmeaBroadcaster> sinks = [];
        try
        {
            if (settings.CreateTcpOptions() is { } tcpOptions)
            {
                sinks.Add(new TcpNmeaBroadcaster(tcpOptions, this.loggerFactory.CreateLogger<TcpNmeaBroadcaster>()));
            }

            if (settings.CreateWebSocketOptions() is { } webSocketOptions)
            {
                sinks.Add(new WebSocketNmeaBroadcaster(webSocketOptions, this.loggerFactory));
            }

            BroadcastPipeline pipeline = new(
                source,
                sinks,
                settings.CreatePipelineOptions(loop),
                this.timeProvider,
                this.loggerFactory.CreateLogger<BroadcastPipeline>());

            // Off the calling thread, so a source that completes synchronously cannot hold up the
            // display. Linked so that a failing display stops the run rather than orphaning it.
            using CancellationTokenSource runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task run = Task.Run(() => pipeline.RunAsync(runCancellation.Token), CancellationToken.None);

            try
            {
                if (!settings.Quiet)
                {
                    await this.ShowStatisticsAsync(pipeline, run, sourceDescription).ConfigureAwait(false);
                }
            }
            catch
            {
                await runCancellation.CancelAsync().ConfigureAwait(false);
                await run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw;
            }

            await run.ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"ais-broadcaster: {ex.Message}");
            return 1;
        }
        finally
        {
            foreach (INmeaBroadcaster sink in sinks)
            {
                await sink.DisposeAsync().ConfigureAwait(false);
            }

            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed:mm\\:ss}"
            : elapsed.ToString("mm\\:ss", CultureInfo.InvariantCulture);

    private static IRenderable BuildTable(BroadcastPipeline pipeline, string sourceDescription, double rate, TimeSpan elapsed)
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]ais-broadcaster[/]")
            .AddColumn(new TableColumn("[grey]Item[/]"))
            .AddColumn(new TableColumn("[grey]Value[/]").RightAligned());

        table.AddRow("Source", Markup.Escape(sourceDescription));
        table.AddRow("Pacing", pipeline.Options.Pacing.ToString());

        foreach (INmeaBroadcaster sink in pipeline.Sinks)
        {
            table.AddRow(Markup.Escape(sink.Name), $"[cyan]{sink.ClientCount}[/] client{(sink.ClientCount == 1 ? string.Empty : "s")}");
        }

        table.AddRow("Sentences broadcast", $"[cyan]{pipeline.SentencesBroadcast:N0}[/]");
        table.AddRow("Sentences / second", $"[cyan]{rate:N1}[/]");
        table.AddRow("Faults injected", pipeline.FaultsInjected == 0 ? "0" : $"[yellow]{pipeline.FaultsInjected:N0}[/]");
        table.AddRow("Loops", pipeline.Loops.ToString("N0", CultureInfo.CurrentCulture));
        table.AddRow("Elapsed", FormatElapsed(elapsed));

        return table;
    }

    private async Task ShowStatisticsAsync(BroadcastPipeline pipeline, Task run, string sourceDescription)
    {
        long started = this.timeProvider.GetTimestamp();

        if (System.Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
        {
            // A live display needs a terminal to redraw in place. Piped or captured output (a log
            // file, a container) gets one plain line per interval instead.
            long previousCount = 0;
            long previousTimestamp = started;
            while (!run.IsCompleted)
            {
                await Task.WhenAny(run, Task.Delay(StatisticsLineInterval, this.timeProvider)).ConfigureAwait(false);
                if (run.IsCompleted)
                {
                    break;
                }

                long now = this.timeProvider.GetTimestamp();
                long count = pipeline.SentencesBroadcast;
                double rate = (count - previousCount) / this.timeProvider.GetElapsedTime(previousTimestamp, now).TotalSeconds;
                previousCount = count;
                previousTimestamp = now;

                string clients = string.Join(", ", pipeline.Sinks.Select(s => $"{s.Name} {s.ClientCount}"));
                System.Console.Out.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{FormatElapsed(this.timeProvider.GetElapsedTime(started, now))} sentences {count} ({rate:F1}/s) faults {pipeline.FaultsInjected} loops {pipeline.Loops} clients [{clients}]"));
            }

            return;
        }

        await AnsiConsole.Live(BuildTable(pipeline, sourceDescription, 0, TimeSpan.Zero))
            .AutoClear(false)
            .StartAsync(async context =>
            {
                long previousCount = pipeline.SentencesBroadcast;
                long previousTimestamp = started;

                while (!run.IsCompleted)
                {
                    await Task.WhenAny(run, Task.Delay(RefreshInterval, this.timeProvider)).ConfigureAwait(false);

                    long now = this.timeProvider.GetTimestamp();
                    long count = pipeline.SentencesBroadcast;
                    double seconds = this.timeProvider.GetElapsedTime(previousTimestamp, now).TotalSeconds;
                    double rate = seconds > 0 ? (count - previousCount) / seconds : 0;
                    previousCount = count;
                    previousTimestamp = now;

                    context.UpdateTarget(BuildTable(pipeline, sourceDescription, rate, this.timeProvider.GetElapsedTime(started, now)));
                    context.Refresh();
                }
            }).ConfigureAwait(false);
    }
}
