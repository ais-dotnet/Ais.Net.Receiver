namespace Ais.Net.Models.Abstractions
{
    public interface IAisMessageType27
    {
        float? CourseOverGroundDegrees { get; }
        
        bool NotGnssPosition { get; }
        
        NavigationStatus NavigationStatus { get; }
        
        Position? Position { get; }
        
        bool PositionAccuracy { get; }
        
        float? SpeedOverGround { get; }
    }
}