namespace Ais.Net.Receiver.Domain
{
    public class AisMessageBase : IVesselIdentity, IAisMessageType
    {
        public uint Mmsi { get; set; }

        public int MessageType { get; set; }
    }
}