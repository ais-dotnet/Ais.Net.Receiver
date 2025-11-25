using System.Text;
using Ais.Net.Receiver.Parser;
using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NmeaMessageExtensionsTests
{
    [TestMethod]
    public void IsMissingNmeaBlockTags_WithTags_ReturnsFalse()
    {
        // Arrange
        string message = @"\s:1000001,c:1637760000*24\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C";
        byte[] bytes = Encoding.ASCII.GetBytes(message);

        // Act
        bool result = bytes.IsMissingNmeaBlockTags();

        // Assert
        result.ShouldBeFalse();
    }

    [TestMethod]
    public void IsMissingNmeaBlockTags_WithoutTags_ReturnsTrue()
    {
        // Arrange
        string message = "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C";
        byte[] bytes = Encoding.ASCII.GetBytes(message);

        // Act
        bool result = bytes.IsMissingNmeaBlockTags();

        // Assert
        result.ShouldBeTrue();
    }

    [TestMethod]
    public void ParseNmeaBlockTags_ValidTags_ReturnsCorrectValues()
    {
        // Arrange
        string message = @"\s:1000001,c:1637760000*24\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C";
        byte[] bytes = Encoding.ASCII.GetBytes(message);

        // Act
        (int stationId, long timestamp) = bytes.ParseNmeaBlockTags();

        // Assert
        stationId.ShouldBe(1000001);
        timestamp.ShouldBe(1637760000);
    }
}