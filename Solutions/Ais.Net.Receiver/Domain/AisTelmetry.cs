namespace Ais.Net.Receiver.Domain
{
    public record AisTelmetry(int MessageType, uint Mmsi, string VesselName) : AisMessageBase(MessageType, Mmsi), IVesselPosition, IVesselName
    {
        public Position? Position { get; init; }
    }
}