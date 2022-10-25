namespace Ais.Net.Receiver.Host.Console
{
    using System;
    using System.Linq;
    using System.Reactive.Linq;
    using Ais.Net.Models.Abstractions;
    using Ais.Net.Receiver.Receiver;

    public static class ReceiverHostExtensions
    {
        public static IObservable<long> RunningCount<T>(this IObservable<T> eventsForCount)
        {
            return eventsForCount.Scan(0L, (total, _) => total + 1);
        }

        public static IObservable<(long message, long sentence, long error)> GetStreamStatistics(this ReceiverHost receiverHost, TimeSpan period)
        {
            IObservable<long> messageRunningCount = receiverHost.Messages.RunningCount();
            IObservable<long> sentencesRunningCount = receiverHost.Sentences.RunningCount();
            IObservable<long> errorRunningCount = receiverHost.Errors.RunningCount();

            IObservable<(long messages, long sentences, long errors)> runningCounts =
                Observable.CombineLatest(
                    messageRunningCount,
                    sentencesRunningCount, 
                    errorRunningCount, 
                    (messages, sentences, errors) => (messages, sentences, errors));

            return runningCounts.Buffer(period)
                                .Select(window => (window.First(), window.Last()))
                                .Select<((long, long, long), (long, long, long)), (long, long, long)>(
                                    (((long messages, long sentences, long errors) first, 
                                      (long messages, long sentences, long errors) last) pair) 
                                        => (pair.last.messages - pair.first.messages, 
                                            pair.last.sentences - pair.first.sentences, 
                                            pair.last.errors - pair.first.errors));
        }

        public static IObservable<(uint mmsi, IVesselNavigation navigation, IVesselName name)> VesselNavigationWithNameStream(this IObservable<IAisMessage> messages)
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
                select (mmsi: perVesselMessages.Key, vesselLocationAndName.navigation, vesselLocationAndName.name);
        }
    }
}