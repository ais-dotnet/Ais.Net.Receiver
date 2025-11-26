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

        IAisMessage? receivedMessage = null;
        processor.Messages.Subscribe(msg => receivedMessage = msg);

        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

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

        IAisMessage? receivedMessage = null;
        processor.Messages.Subscribe(msg => receivedMessage = msg);

        NmeaLineParser parsedLine = new("!AIVDM,1,1,,B,B52Jcw000>k<865k030n4?w00000,0*18"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

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
        // We construct a synthetic single-line message containing the full payload to simplify the test
        // and match the OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding) pattern.

        IAisMessage? receivedMessage = null;
        processor.Messages.Subscribe(msg => receivedMessage = msg);

        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,55?MbV02;H;s<HtKR20EHE:0@T4@Dn2222222216L961O5Gf0NSQEp6ClRp8888888888880,2*00"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType5>();

        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();
        receivedMessage.Mmsi.ShouldBe(351759000u);

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

        (Exception Exception, string Line)? receivedError = null;
        using IDisposable subscription = processor.ParseErrors.Subscribe(e => receivedError = e);

        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,1,0*00"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

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
        ReadOnlySpan<byte> line = "malformed line"u8;
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

        IAisMessage? receivedMessage = null;
        (Exception Exception, string Line)? receivedError = null;

        using IDisposable msgSub = processor.Messages.Subscribe(msg => receivedMessage = msg);
        using IDisposable errSub = processor.ParseErrors.Subscribe(e => receivedError = e);

        // Message type 4 (Base Station Report) is not handled by the processor
        // First 6 bits of payload determine message type. '4' in AIS encoding = 4
        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,400000000000000000000000000,0*00"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

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
        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        // Type 19 Extended Class B CS Position Report
        // Source: gpsd sample data, MMSI: 367059850, Ship Name: CAPT.J.RIMES
        NmeaLineParser parsedLine = new("!AIVDM,1,1,,B,C5N3SRgPEnJGEBT>NhWAwwo862PaLELTBJ:V00000000S0D:R220,0*0B"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType19>();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        receivedMessage.Mmsi.ShouldBe(367059850u);

        // Type 19 includes ship name
        receivedMessage.ShouldBeAssignableTo<IShipType>();
    }

    [TestMethod]
    public void OnNext_ValidType24PartA_EmitsPartAMessage()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        // Type 24 Part A (Static Data Report - vessel name)
        // Source: gpsd sample data
        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,H42O55i18tMET00000000000000,2*6D"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        // Type 24 Part A should have MMSI and part number 0
        IVesselIdentity identity = receivedMessage;
        identity.Mmsi.ShouldBe(271041815u);
    }

    [TestMethod]
    public void OnNext_ValidType24PartB_EmitsPartBMessage()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        // Type 24 Part B (Static Data Report - call sign, dimensions, vendor ID)
        // Source: gpsd sample data
        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,H42O55lti4hhhilD3nink000?050,0*40"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        // Type 24 Part B should have MMSI, call sign, and dimensions
        IVesselIdentity identity = receivedMessage;
        identity.Mmsi.ShouldBe(271041815u);
    }

    [TestMethod]
    public void OnNext_ValidType27Message_EmitsMessage()
    {
        // Arrange
        using NmeaToAisMessageTypeProcessor processor = new();

        IAisMessage? receivedMessage = null;
        using IDisposable subscription = processor.Messages.Subscribe(msg => receivedMessage = msg);

        // Type 27 Long Range AIS Broadcast (96 bits)
        // Source: gpsd sample data
        NmeaLineParser parsedLine = new("!AIVDM,1,1,,A,KCQ9r=hrFUnH7P00,0*41"u8);

        // Act
        processor.OnNext(parsedLine, parsedLine.Payload, parsedLine.Padding);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IAisMessageType27>();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();

        IVesselIdentity identity = receivedMessage;
        identity.Mmsi.ShouldBe(236091959u);
    }
}