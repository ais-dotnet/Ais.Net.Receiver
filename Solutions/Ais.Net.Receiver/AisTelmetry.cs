namespace Ais.Net.Receiver
{
    public class AisTelmetry
    {
        public string VesselName { get; set; }

        public uint Mmsi { get; set; }

        public LonLat Position { get; set; }
    }

    public record LonLat
    {
        public double? Longitude { get; init; }

        public double? Latitude { get; init; }
    }
}