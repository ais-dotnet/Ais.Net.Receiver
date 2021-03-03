namespace Ais.Net.Receiver.Domain
{
    public record AisMessageBase(int MessageType, uint Mmsi) : IVesselIdentity, IAisMessageType;
}