using Shouldly;
using Ais.Net.Receiver.Parser;
using Ais.Net.Models.Abstractions;
using System.Text;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NmeaToAisMessageTypeProcessorTests
{
    [TestMethod]
    public void OnNext_ValidType1Message_EmitsMessage()
    {
        // Arrange
        NmeaToAisMessageTypeProcessor processor = new();
        string payload = "13u?etPv2;0n:dDPwUM1U1Cb069D";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;
            
        IAisMessage? receivedMessage = null;
        processor.Messages.Subscribe(msg => receivedMessage = msg);

        byte[] line = Encoding.ASCII.GetBytes("!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24");
        NmeaLineParser parsedLine = new(line);
            
        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();
        IVesselIdentity? identity = receivedMessage;
        identity.Mmsi.ShouldBe(265547250u);
    }

    [TestMethod]
    public void OnNext_ValidType18Message_EmitsMessage()
    {
        // Arrange
        NmeaToAisMessageTypeProcessor processor = new();
        string payload = "B52Jcw000>k<865k030n4?w00000";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;
            
        IAisMessage? receivedMessage = null;
        processor.Messages.Subscribe(msg => receivedMessage = msg);

        byte[] line = "!AIVDM,1,1,,B,B52Jcw000>k<865k030n4?w00000,0*18"u8.ToArray();
        NmeaLineParser parsedLine = new(line);
            
        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType18>();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();
        IVesselIdentity? identity = receivedMessage;
        identity.Mmsi.ShouldBe(338078716u); // Decoded MMSI for this payload
    }

    [TestMethod]
    public void OnNext_ValidType5Message_EmitsMessage()
    {
        // Arrange
        NmeaToAisMessageTypeProcessor processor = new();
        // Type 5 payload (Static and Voyage Related Data)
        // MMSI: 351759000, Ship Name: "EVER GIVEN"
        string payload = "55?MbV02;H;s<HtKR20EHE:0@T4@Dn2222222216L961O5Gf0NSQEp6ClRp8888888888880";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 2; // Type 5 often has padding
            
        IAisMessage? receivedMessage = null;
        processor.Messages.Subscribe(msg => receivedMessage = msg);

        // We need a dummy line parser, though for this processor it might not use it for the payload parsing itself
        // but it passes it to the event.
        byte[] line = "!AIVDM,2,1,9,A,55?MbV02;H;s<HtKR20EHE:0@T4@Dn2222222216L961O5Gf0NSQEp6ClRp888,0*1C"u8.ToArray();
        NmeaLineParser parsedLine = new(line);
            
        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType5>();
            
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();
        ((IVesselIdentity)receivedMessage).Mmsi.ShouldBe(351759000u);

        receivedMessage.ShouldBeAssignableTo<IVesselName>();
        ((IVesselName)receivedMessage).VesselName.ShouldBe("EVER DIADEM         ");
    }
}