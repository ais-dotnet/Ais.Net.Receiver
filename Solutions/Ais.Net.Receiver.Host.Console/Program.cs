// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Host.Console
{
    using System;
    using System.Reactive.Linq;
    using System.Threading.Tasks;

    using Ais.Net.Models;
    using Ais.Net.Models.Abstractions;
    using Ais.Net.Receiver.Configuration;
    using Ais.Net.Receiver.Receiver;

    using Microsoft.Extensions.Configuration;

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                            .AddJsonFile("settings.json", true, true)
                            .AddJsonFile("settings.local.json", true, true)
                            .Build();

            AisConfig aisConfig = config.GetSection("Ais").Get<AisConfig>();
            var receiverHost = new ReceiverHost(aisConfig);

            // Write out the messages as they are received over the wire.
            receiverHost.Sentences.Subscribe((sentences) => Console.WriteLine(sentences));

            // Decode teh sentences into messages, and group by the vessel by Id
            IObservable<IGroupedObservable<uint, IAisMessage>> byVessel = receiverHost.Messages.GroupBy(m => m.Mmsi);

            // Combine the various message types required to create a stream containing name and navigation
            IObservable<(uint mmsi, IVesselNavigation navigation, IVesselName name)>? vesselNavigationWithNameStream =
                from perVesselMessages in byVessel
                let vesselNavigationUpdates = perVesselMessages.OfType<IVesselNavigation>()
                let vesselNames = perVesselMessages.OfType<IVesselName>()
                let vesselLocationsWithNames = vesselNavigationUpdates.CombineLatest(vesselNames, (navigation, name) => (navigation, name))
                from vesselLocationAndName in vesselLocationsWithNames
                select (mmsi: perVesselMessages.Key, vesselLocationAndName.navigation, vesselLocationAndName.name);

            vesselNavigationWithNameStream.Subscribe(navigationWithName =>
            {
                (uint mmsi, IVesselNavigation navigation, IVesselName name) = navigationWithName;
                string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";
                Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}] - [{navigation.CourseOverGroundDegrees ?? 0}]");
            });

            await receiverHost.StartAsync();
        }
    }
}