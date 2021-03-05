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

            var aisConfig = config.GetSection("Ais").Get<AisConfig>();

            var receiverHost = new ReceiverHost(aisConfig);

            IObservable<IGroupedObservable<uint, AisMessageBase>> byVessel = receiverHost.Telemetry.GroupBy(m => m.Mmsi);

            var xs =
                from vg in byVessel
                let vesselNavigation = vg.OfType<IVesselNavigation>()
                let vesselNames = vg.OfType<IVesselName>()
                let vesselLocationsWithNames = vesselNavigation.CombineLatest(vesselNames)
                from locationAndName in vesselLocationsWithNames
                select (mmsi: vg.Key, location: locationAndName.First, name: locationAndName.Second);

            xs.Subscribe(ln =>
            {
                (uint mmsi, IVesselNavigation navigation, IVesselName name) = ln;
                string positionText = navigation.Position is null ? "unknown position" : $"{navigation.Position.Latitude},{navigation.Position.Longitude}";
                Console.WriteLine($"[{mmsi}: '{name.VesselName.CleanVesselName()}'] - [{positionText}] - [{navigation.CourseOverGroundDegrees}]");
            });

            await receiverHost.StartAsync();
        }
    }

    /*
    var storageConfig = config.GetSection("Storage").Get<StorageConfig>();
    IStorageClient storageClient = new StorageClient(storageConfig);
    */
}