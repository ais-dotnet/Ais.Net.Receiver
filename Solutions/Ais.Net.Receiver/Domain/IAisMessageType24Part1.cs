namespace Ais.Net.Receiver.Domain
{
    public interface IAisMessageType24Part1
    {
        string CallSign { get; set; }
        uint MothershipMmsi { get; set; }
        uint SerialNumber { get; set; }
        uint Spare162 { get; set; }
        uint UnitModelCode { get; set; }
        string VendorIdRev3 { get; set; }
        string VendorIdRev4 { get; set; }
    }
}