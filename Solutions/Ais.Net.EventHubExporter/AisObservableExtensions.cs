namespace Ais.Net.EventHubExporter
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Ais.Net.Models.Abstractions;

    using Endjin.Reactor.EventData.Ais;

    internal static class AisObservableExtensions
    {
        public static IObservable<AisMessage> ConvertToDataModel(this IObservable<IAisMessage> source)
        {
            return from msg in source
                   where msg is (IVesselName or IVesselNavigation)
                   select msg switch
                   {
                       IVesselName n =>
                          new AisMessage
                          {
                              Type = MessageType.Name,
                              Mmsi = msg.Mmsi,
                              Name = new VesselName()
                              {
                                  Name = n.VesselName
                              }
                          },
                       IVesselNavigation n =>
                          new AisMessage
                          {
                              Type = MessageType.Navigation,
                              Mmsi = msg.Mmsi,
                              Navigation = new VesselNavigation()
                              {
                                  CourseOverGroundDegrees = n.CourseOverGroundDegrees,
                                  Position = n.Position switch
                                  {
                                      Position p => new Coordinates() { Latitude = p.Latitude, Longitude = p.Longitude },
                                      null => null
                                  }
                              }
                          },
                       _ => null
                   };
        }
    }
}
