namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType19
    {
        bool IsDteNotReady { get; set; }
        EpfdFixType PositionFixType { get; set; }
        int RegionalReserved139 { get; set; }
        int RegionalReserved38 { get; set; }
        string ShipName { get; set; }
        uint Spare308 { get; set; }
    }
}