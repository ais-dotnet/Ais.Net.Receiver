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
        ReadOnlySpan<byte> bytes = @"\s:1000001,c:1637760000*24\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8;

        // Act
        bool result = bytes.IsMissingNmeaBlockTags;

        // Assert
        result.ShouldBeFalse();
    }

    [TestMethod]
    public void IsMissingNmeaBlockTags_WithoutTags_ReturnsTrue()
    {
        // Arrange
        ReadOnlySpan<byte> bytes = "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8;

        // Act
        bool result = bytes.IsMissingNmeaBlockTags;

        // Assert
        result.ShouldBeTrue();
    }

    [TestMethod]
    public void ParseNmeaBlockTags_ValidTags_ReturnsCorrectValues()
    {
        // Arrange
        ReadOnlySpan<byte> bytes = @"\s:1000001,c:1637760000*24\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8;

        // Act
        (int stationId, long timestamp) = bytes.ParseNmeaBlockTags();

        // Assert
        stationId.ShouldBe(1000001);
        timestamp.ShouldBe(1637760000);
    }

    [TestMethod]
    public void PrependNmeaBlockTags_AddsTagsToMessage()
    {
        // Arrange
        string message = "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C";
        ReadOnlyMemory<byte> memory = "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8.ToArray();

        // Act
        ReadOnlyMemory<byte> result = memory.PrependNmeaBlockTags();

        // Assert
        string resultStr = Encoding.ASCII.GetString(result.Span);
        resultStr.ShouldStartWith("\\s:");
        resultStr.ShouldContain(",c:");
        resultStr.ShouldContain(message);
    }

    [TestMethod]
    public void IsMissingNmeaBlockTags_EmptyArray_ReturnsFalse()
    {
        // Arrange
        byte[] bytes = [];

        // Act
        bool result = ((ReadOnlySpan<byte>)bytes).IsMissingNmeaBlockTags;

        // Assert - empty array returns false (no message to check)
        result.ShouldBeFalse();
    }

    [TestMethod]
    public void ParseNmeaBlockTags_MessageWithoutTags_ReturnsZeroValues()
    {
        // Arrange
        ReadOnlySpan<byte> bytes = "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8;

        // Act
        (int stationId, long timestamp) = bytes.ParseNmeaBlockTags();

        // Assert - should return zeros when no block tags present
        stationId.ShouldBe(0);
        timestamp.ShouldBe(0);
    }

    [TestMethod]
    public void ParseNmeaBlockTags_OnlyStationId_ReturnsStationIdAndZeroTimestamp()
    {
        // Arrange - only station ID, no timestamp
        ReadOnlySpan<byte> bytes = @"\s:1234567*00\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8;

        // Act
        (int stationId, long timestamp) = bytes.ParseNmeaBlockTags();

        // Assert
        stationId.ShouldBe(1234567);
        timestamp.ShouldBe(0);
    }

    [TestMethod]
    public void IsMissingNmeaBlockTags_StartsWithBackslash_ReturnsFalse()
    {
        // Arrange - message starting with backslash is considered to have tags
        ReadOnlySpan<byte> bytes = @"\anything here"u8;

        // Act
        bool result = bytes.IsMissingNmeaBlockTags;

        // Assert
        result.ShouldBeFalse();
    }
}