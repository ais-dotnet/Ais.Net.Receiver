namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType24Part1
    {
        string CallSign { get; }
        uint MothershipMmsi { get; }
        uint SerialNumber { get; }
        uint Spare162 { get; }
        uint UnitModelCode { get; }
        string VendorIdRev3 { get; }
        string VendorIdRev4 { get; }
    }
}