// <copyright file="ReceiverHostExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Host.Console
{
    using System;
    using System.Linq;
    using System.Reactive.Linq;
    using Ais.Net.Models.Abstractions;
    using Ais.Net.Receiver.Receiver;

    /// <summary>
    /// Extensions for the <see cref="ReceiverHost"/> and its data streams.
    /// </summary>
    public static class ReceiverHostExtensions
    {
        /// <summary>
        /// Calculates statistics about the number of <see cref="ReceiverHost.Messages"/>, <see cref="ReceiverHost.Sentences"/> and <see cref="ReceiverHost.Errors"/>
        /// generated during the specified <paramref name="period"/>.
        /// </summary>
        /// <param name="receiverHost">The <see cref="ReceiverHost"/> to extend.</param>
        /// <param name="period">The duration statistics should be collected for, before returning.</param>
        /// <returns>An observable sequence of tuple containing statistics.</returns>
        public static IObservable<(long Message, long Sentence, long Error)> GetStreamStatistics(this ReceiverHost receiverHost, TimeSpan period)
        {
            IObservable<(long Messages, long Sentences, long Errors)> runningCounts =
                receiverHost.Messages.RunningCount().CombineLatest(
                    receiverHost.Sentences.RunningCount(),
                    receiverHost.Errors.RunningCount(),
                    (messages, sentences, errors) => (messages, sentences, errors));

            return runningCounts.Buffer(period)
                                .Select(window => (window[0], window[^1]))
                                .Select<((long, long, long), (long, long, long)), (long, long, long)>(
                                    (((long Messages, long Sentences, long Errors) First,
                                      (long Messages, long Sentences, long Errors) Last) pair)
                                        => (pair.Last.Messages - pair.First.Messages,
                                            pair.Last.Sentences - pair.First.Sentences,
                                            pair.Last.Errors - pair.First.Errors));
        }

        /// <summary>
        /// Groups and combines the <see cref="IAisMessage">AIS Messages</see> so that vessel name and navigation information can be displayed.
        /// </summary>
        /// <param name="messages">An observable stream of <see cref="IAisMessage">AIS Messages</see>.</param>
        /// <returns>An observable sequence of tuple containing vessel information.</returns>
        public static IObservable<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)> VesselNavigationWithNameStream(this IObservable<IAisMessage> messages)
        {
            // Decode the sentences into messages, and group by the vessel by Id
            IObservable<IGroupedObservable<uint, IAisMessage>> byVessel = messages.GroupBy(m => m.Mmsi);

            // Combine the various message types required to create a stream containing name and navigation
            return
                from perVesselMessages in byVessel
                let vesselNavigationUpdates = perVesselMessages.OfType<IVesselNavigation>()
                let vesselNames = perVesselMessages.OfType<IVesselName>()
                let vesselLocationsWithNames = vesselNavigationUpdates.CombineLatest(vesselNames, (navigation, name) => (navigation, name))
                from vesselLocationAndName in vesselLocationsWithNames
                select (Mmsi: perVesselMessages.Key, vesselLocationAndName.navigation, vesselLocationAndName.name);
        }

        /// <summary>
        /// Provides a running count of events provided by an observable stream.
        /// </summary>
        /// <typeparam name="T">Type of events to count.</typeparam>
        /// <param name="eventsForCount">Observable stream of events to count.</param>
        /// <returns>An observable sequence representing the count of events.</returns>
        private static IObservable<long> RunningCount<T>(this IObservable<T> eventsForCount)
        {
            return eventsForCount.Scan(0L, (total, _) => total + 1);
        }
    }
}