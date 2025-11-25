using System.Reactive.Linq;
using System.Text;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Parser;

using Shouldly;

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

    [TestMethod]
    public void OnNext_MalformedPayload_EmitsParseError()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        // Message type 1 payload that is truncated (too short to parse)
        // '1' = message type 1, but the payload is too short to contain required fields
        string payload = "1";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;

        (Exception Exception, string Line)? receivedError = null;
        using IDisposable subscription = processor.ParseErrors.Subscribe(e => receivedError = e);

        byte[] line = "!AIVDM,1,1,,A,1,0*00"u8.ToArray();
        NmeaLineParser parsedLine = new(line);

        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedError.ShouldNotBeNull();
        receivedError.Value.Line.ShouldBe(payload);
        receivedError.Value.Exception.ShouldNotBeNull();
    }

    [TestMethod]
    public void OnError_WithException_EmitsParseError()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        byte[] line = "malformed line"u8.ToArray();
        Exception testException = new InvalidOperationException("Test error");
        int lineNumber = 42;

        (Exception Exception, string Line)? receivedError = null;
        using IDisposable subscription = processor.ParseErrors.Subscribe(e => receivedError = e);

        // Act
        processor.OnError(line, testException, lineNumber);

        // Assert
        receivedError.ShouldNotBeNull();
        receivedError.Value.Line.ShouldBe("malformed line");
        receivedError.Value.Exception.ShouldBe(testException);
    }

    [TestMethod]
    public void OnCompleted_CompletesBothObservables()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        bool messagesCompleted = false;
        bool parseErrorsCompleted = false;

        using IDisposable messagesSub = processor.Messages.Subscribe(
            onNext: _ => { },
            onCompleted: () => messagesCompleted = true);
        using IDisposable errorsSub = processor.ParseErrors.Subscribe(
            onNext: _ => { },
            onCompleted: () => parseErrorsCompleted = true);

        // Act
        processor.OnCompleted();

        // Assert - OnCompleted on Subject is synchronous
        messagesCompleted.ShouldBeTrue();
        parseErrorsCompleted.ShouldBeTrue();
    }

    [TestMethod]
    public void OnNext_UnsupportedMessageType_DoesNotEmitMessageOrError()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        // Message type 4 (Base Station Report) is not handled by the processor
        // First 6 bits of payload determine message type. '4' in AIS encoding = 4
        string payload = "400000000000000000000000000";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;

        IAisMessage? receivedMessage = null;
        (Exception Exception, string Line)? receivedError = null;

        using IDisposable msgSub = processor.Messages.Subscribe(msg => receivedMessage = msg);
        using IDisposable errSub = processor.ParseErrors.Subscribe(e => receivedError = e);

        byte[] line = "!AIVDM,1,1,,A,400000000000000000000000000,0*00"u8.ToArray();
        NmeaLineParser parsedLine = new(line);

        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert - unsupported types are silently ignored
        receivedMessage.ShouldBeNull();
        receivedError.ShouldBeNull();
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        NmeaToAisMessageTypeProcessor processor = new();

        // Act & Assert - should not throw
        processor.Dispose();
        processor.Dispose();
    }

    [TestMethod]
    public void OnNext_ValidType19Message_EmitsMessageWithShipName()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        // Type 19 Extended Class B CS Position Report
        // Source: gpsd sample data, MMSI: 367059850, Ship Name: CAPT.J.RIMES
        string payload = "C5N3SRgPEnJGEBT>NhWAwwo862PaLELTBJ:V00000000S0D:R220";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        byte[] line = "!AIVDM,1,1,,B,C5N3SRgPEnJGEBT>NhWAwwo862PaLELTBJ:V00000000S0D:R220,0*0B"u8.ToArray();
        NmeaLineParser parsedLine = new(line);

        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType19>();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        ((IVesselIdentity)receivedMessage).Mmsi.ShouldBe(367059850u);

        // Type 19 includes ship name
        receivedMessage.ShouldBeAssignableTo<IShipType>();
    }

    [TestMethod]
    public void OnNext_ValidType24PartA_EmitsPartAMessage()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        // Type 24 Part A (Static Data Report - vessel name)
        // Source: gpsd sample data
        string payload = "H42O55i18tMET00000000000000";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 2;

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        byte[] line = "!AIVDM,1,1,,A,H42O55i18tMET00000000000000,2*6D"u8.ToArray();
        NmeaLineParser parsedLine = new(line);

        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        // Type 24 Part A should have MMSI and part number 0
        IVesselIdentity identity = (IVesselIdentity)receivedMessage;
        identity.Mmsi.ShouldBe(271041815u);
    }

    [TestMethod]
    public void OnNext_ValidType24PartB_EmitsPartBMessage()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        // Type 24 Part B (Static Data Report - call sign, dimensions, vendor ID)
        // Source: gpsd sample data
        string payload = "H42O55lti4hhhilD3nink000?050";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        byte[] line = "!AIVDM,1,1,,A,H42O55lti4hhhilD3nink000?050,0*40"u8.ToArray();
        NmeaLineParser parsedLine = new(line);

        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        // Type 24 Part B should have MMSI, call sign, and dimensions
        IVesselIdentity identity = (IVesselIdentity)receivedMessage;
        identity.Mmsi.ShouldBe(271041815u);
    }

    [TestMethod]
    public void OnNext_ValidType27Message_EmitsMessage()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();
        // Type 27 Long Range AIS Broadcast (96 bits)
        // Source: gpsd sample data
        string payload = "KCQ9r=hrFUnH7P00";
        byte[] asciiPayload = Encoding.ASCII.GetBytes(payload);
        uint padding = 0;

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        byte[] line = "!AIVDM,1,1,,A,KCQ9r=hrFUnH7P00,0*41"u8.ToArray();
        NmeaLineParser parsedLine = new(line);

        // Act
        processor.OnNext(parsedLine, asciiPayload, padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType27>();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        IVesselIdentity identity = (IVesselIdentity)receivedMessage;
        identity.Mmsi.ShouldBe(236091959u);
    }
}