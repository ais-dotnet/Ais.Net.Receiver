namespace Ais.Net.Receiver.Domain
{
    public interface IVesselNavigation
    {
        uint CourseOverGround10thDegrees { get; set; }
        bool PositionAccuracy { get; set; }
        uint SpeedOverGroundTenths { get; set; }
        uint TimeStampSecond { get; set; }
        uint TrueHeadingDegrees { get; set; }
    }
}