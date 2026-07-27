// <copyright file="ReceiverHostExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Telemetry;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// Extensions for the <see cref="ReceiverHost"/> and its data streams.
/// </summary>
public static class ReceiverHostExtensions
{
    extension(ReceiverHost receiverHost)
    {
        /// <summary>
        /// Calculates statistics about the number of <see cref="ReceiverHost.Messages"/>, <see cref="ReceiverHost.Sentences"/> and <see cref="ReceiverHost.Errors"/>
        /// generated during the specified <paramref name="period"/>.
        /// </summary>
        /// <param name="period">The duration statistics should be collected for, before returning.</param>
        /// <param name="scheduler">Optional scheduler to use for time-based operations.</param>
        /// <returns>An observable sequence of tuple containing statistics.</returns>
        public IObservable<(long Message, long Sentence, long Error)> GetStreamStatistics(TimeSpan period, IScheduler? scheduler = null)
        {
            IObservable<(long Messages, long Sentences, long Errors)> runningCounts =
                receiverHost.Messages.RunningCount().CombineLatest(
                    receiverHost.RawSentences.RunningCount(),
                    receiverHost.Errors.RunningCount(),
                    (messages, sentences, errors) => (messages, sentences, errors));

            return runningCounts.Buffer(period, scheduler ?? Scheduler.Default)
                .Scan(
                    (LastMessages: 0L, LastSentences: 0L, LastErrors: 0L, Message: 0L, Sentence: 0L, Error: 0L),
                    (state, window) =>
                    {
                        // The running totals are cumulative, so a period's count is the total at the end
                        // of this window minus the total at the end of the previous window. Diffing
                        // within a single window (as the old code did) dropped the window's first event
                        // and reported zero for any single-event window. An empty window (an idle period)
                        // carries the previous totals forward and reports zeros.
                        (long messages, long sentences, long errors) = window.Count > 0
                            ? window[^1]
                            : (state.LastMessages, state.LastSentences, state.LastErrors);

                        return (
                            messages,
                            sentences,
                            errors,
                            messages - state.LastMessages,
                            sentences - state.LastSentences,
                            errors - state.LastErrors);
                    })
                .Select(state => (Message: state.Message, Sentence: state.Sentence, Error: state.Error));
        }
    }

    extension(IObservable<IAisMessage> messages)
    {
        /// <summary>
        /// Groups and combines the <see cref="IAisMessage">AIS Messages</see> so that vessel name and navigation information can be displayed.
        /// </summary>
        /// <param name="inactivityTimeout">
        /// Optional timeout for inactive vessel groups. When a vessel hasn't sent any messages for this duration,
        /// its group is disposed to prevent memory accumulation. Defaults to 30 minutes if not specified.
        /// </param>
        /// <param name="scheduler">Optional scheduler for the inactivity timeout (defaults to <see cref="Scheduler.Default"/>).</param>
        /// <param name="instrumentation">
        /// Optional instrumentation. When supplied, the per-vessel group lifecycle is surfaced as
        /// telemetry: a vessel appearing in the stream for the first time emits a
        /// <c>VesselDetected</c> span, and a vessel going quiet for <paramref name="inactivityTimeout"/>
        /// emits a <c>VesselTrackLost</c> span. This grouping already owns that lifecycle, so it is the
        /// only place the two events can be observed without tracking vessel state a second time.
        /// </param>
        /// <returns>An observable sequence of tuple containing vessel information.</returns>
        public IObservable<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)> VesselNavigationWithNameStream(
            TimeSpan? inactivityTimeout = null,
            IScheduler? scheduler = null,
            ApplicationInstrumentation? instrumentation = null)
        {
            TimeSpan timeout = inactivityTimeout ?? TimeSpan.FromMinutes(30);
            IScheduler timeoutScheduler = scheduler ?? Scheduler.Default;

            // Decode the sentences into messages, and group by the vessel by Id
            // Use GroupByUntil to automatically dispose groups after inactivity timeout
            IObservable<IGroupedObservable<uint, IAisMessage>> byVessel = messages
                .GroupByUntil(
                    m => m.Mmsi,
                    group =>
                    {
                        // The duration selector runs once per group, at the moment the group is
                        // created - i.e. the first time this MMSI is seen.
                        if (instrumentation is not null)
                        {
                            using Activity? detected = instrumentation.ActivitySource.StartActivity("VesselDetected");
                            detected?.RecordVesselDetected(group.Key);
                        }

                        return group.Throttle(timeout, timeoutScheduler)
                            .Do(_ =>
                            {
                                // Throttle fires one timeout after the vessel's last message, which
                                // closes the group; that is the track-lost transition.
                                if (instrumentation is not null)
                                {
                                    using Activity? lost = instrumentation.ActivitySource.StartActivity("VesselTrackLost");
                                    lost?.RecordVesselTrackLost(
                                        group.Key,
                                        timeoutScheduler.Now - timeout,
                                        timeout.TotalSeconds);
                                }
                            });
                    });

            // Combine the various message types required to create a stream containing name and navigation
            return
                from perVesselMessages in byVessel
                let vesselNavigationUpdates = perVesselMessages.OfType<IVesselNavigation>()
                let vesselNames = perVesselMessages.OfType<IVesselName>()
                let vesselLocationsWithNames = vesselNavigationUpdates.CombineLatest(vesselNames, (navigation, name) => (navigation, name))
                from vesselLocationAndName in vesselLocationsWithNames
                select (Mmsi: perVesselMessages.Key, vesselLocationAndName.navigation, vesselLocationAndName.name);
        }
    }

    /// <summary>
    /// Subscribes to <paramref name="source"/> and feeds each item to <paramref name="tryConsume"/>.
    /// When the consumer declines an item (returns <see langword="false"/> — e.g. a bounded dataflow
    /// block at capacity), <paramref name="onDropped"/> is invoked so the drop is surfaced rather than
    /// lost silently.
    /// </summary>
    /// <typeparam name="T">The stream item type.</typeparam>
    /// <param name="source">The source stream.</param>
    /// <param name="tryConsume">Consumes an item; returns <see langword="false"/> if it was declined.</param>
    /// <param name="onDropped">Invoked once per declined item.</param>
    /// <returns>The subscription.</returns>
    public static IDisposable SubscribeWithBackpressure<T>(
        this IObservable<T> source,
        Func<T, bool> tryConsume,
        Action onDropped) =>
        source.Subscribe(item =>
        {
            if (!tryConsume(item))
            {
                onDropped();
            }
        });

    /// <summary>
    /// Provides a running count of events provided by an observable stream.
    /// </summary>
    /// <typeparam name="T">Type of events to count.</typeparam>
    /// <param name="eventsForCount">Observable stream of events to count.</param>
    /// <returns>An observable sequence representing the count of events.</returns>
    private static IObservable<long> RunningCount<T>(this IObservable<T> eventsForCount) => eventsForCount.Scan(0L, (total, _) => total + 1).StartWith(0L);
}
