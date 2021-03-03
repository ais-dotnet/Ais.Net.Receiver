namespace Ais.Net.Receiver.Domain
{
    public record Position
    {
        public double? Longitude { get; init; }

        public double? Latitude { get; init; }
    }
}