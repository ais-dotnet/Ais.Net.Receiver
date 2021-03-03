namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType19
    {
        bool IsDteNotReady { get; }
        EpfdFixType PositionFixType { get; }
        int RegionalReserved139 { get; }
        int RegionalReserved38 { get; }
        string ShipName { get; }
        uint Spare308 { get; }
    }
}