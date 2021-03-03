namespace Ais.Net.Receiver.Domain
{
    public record AisMessageType5(
        uint Mmsi,
        uint ImoNumber,
        string CallSign,
        string Destination,
        string VesselName,
        ShipType ShipType,
        uint DimensionToBow,
        uint DimensionToPort,
        uint DimensionToStarboard,
        uint DimensionToStern,
        uint Draught10thMetres,
        EpfdFixType PositionFixType) :
            AisMessageBase(MessageType: 5, Mmsi),
            IVesselName,
            IVesselDimensions;
}
