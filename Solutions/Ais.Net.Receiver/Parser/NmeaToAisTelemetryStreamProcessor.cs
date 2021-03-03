namespace Ais.Net.Receiver.Parser
{
    using System;
    using System.Reactive.Subjects;
    using Ais.Net.Receiver.Domain;

    public class NmeaToAisTelemetryStreamProcessor : INmeaAisMessageStreamProcessor
    {
        private readonly Subject<AisMessageBase> telemetry = new();

        public IObservable<AisMessageBase> Telemetry => this.telemetry;

        public void OnNext(in NmeaLineParser parsedLine, in ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            int messageType = NmeaPayloadParser.PeekMessageType(asciiPayload, padding);

            try
            {
                switch (messageType)
                {
                    case >= 1 and <= 3:
                    {
                        this.ParseMessageTypes1Through3(asciiPayload, padding, messageType);
                        return;
                    }
                    case 18:
                    {
                        this.ParseMessageType18(asciiPayload, padding);
                        return;
                    }
                    case 19:
                    {
                        this.ParseMessageType19(asciiPayload, padding);
                        return;
                    }
                    case 24:
                    {
                        this.ParseMessageType24(asciiPayload, padding);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{messageType}] {e.Message}");
            }
        }

        private void ParseMessageTypes1Through3(ReadOnlySpan<byte> asciiPayload, uint padding, int messageType)
        {
            var message = new AisMessageType1Through3 { MessageType = Convert.ToInt16(messageType) };
            var parser = new NmeaAisPositionReportClassAParser(asciiPayload, padding);

            message.CourseOverGround10thDegrees = parser.CourseOverGround10thDegrees;
            message.ManoeuvreIndicator = parser.ManoeuvreIndicator;
            message.MessageType = checked((int)parser.MessageType);
            message.NavigationStatus = parser.NavigationStatus;
            message.PositionAccuracy = parser.PositionAccuracy;
            message.RadioSlotTimeout = parser.RadioSlotTimeout;
            message.RadioSubMessage = parser.RadioSubMessage;
            message.RadioSyncState = parser.RadioSyncState;
            message.RaimFlag = parser.RaimFlag;
            message.RateOfTurn = parser.RateOfTurn;
            message.RepeatIndicator = parser.RepeatIndicator;
            message.SpareBits145 = parser.SpareBits145;
            message.SpeedOverGroundTenths = parser.SpeedOverGroundTenths;
            message.TimeStampSecond = parser.TimeStampSecond;
            message.TrueHeadingDegrees = parser.TrueHeadingDegrees;
            message.Mmsi = parser.Mmsi;
            message.Position = new Position
            {
                Latitude = parser.Latitude10000thMins.From10000thMinsToDegrees(),
                Longitude = parser.Longitude10000thMins.From10000thMinsToDegrees()
            };

            this.telemetry.OnNext(message);
        }

        private void ParseMessageType18(ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            var message = new AisMessageType18 { MessageType = 18 };
            var parser = new NmeaAisPositionReportClassBParser(asciiPayload, padding);

            message.CanAcceptMessage22ChannelAssignment = parser.CanAcceptMessage22ChannelAssignment;
            message.CanSwitchBands = parser.CanSwitchBands;
            message.CourseOverGround10thDegrees = parser.CourseOverGround10thDegrees;
            message.CsUnit = parser.CsUnit;
            message.HasDisplay = parser.HasDisplay;
            message.IsAssigned = parser.IsAssigned;
            message.IsDscAttached = parser.IsDscAttached;
            message.MessageType = checked((int)parser.MessageType);
            message.PositionAccuracy = parser.PositionAccuracy;
            message.RadioStatusType = parser.RadioStatusType;
            message.RaimFlag = parser.RaimFlag;
            message.RegionalReserved139 = parser.RegionalReserved139;
            message.RegionalReserved38 = parser.RegionalReserved38;
            message.RepeatIndicator = parser.RepeatIndicator;
            message.SpeedOverGroundTenths = parser.SpeedOverGroundTenths;
            message.TimeStampSecond = parser.TimeStampSecond;
            message.TrueHeadingDegrees = parser.TrueHeadingDegrees;
            message.Mmsi = parser.Mmsi;
            message.Position = new Position
            {
                Latitude = parser.Latitude10000thMins.From10000thMinsToDegrees(),
                Longitude = parser.Longitude10000thMins.From10000thMinsToDegrees()
            };

            this.telemetry.OnNext(message);
        }

        private void ParseMessageType19(ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            var message = new AisMessageType19 { MessageType = 19 };
            var parser = new NmeaAisPositionReportExtendedClassBParser(asciiPayload, padding);
            Span<byte> shipNameAscii = stackalloc byte[(int) parser.ShipName.CharacterCount];
            parser.ShipName.WriteAsAscii(shipNameAscii);

            message.CourseOverGround10thDegrees = parser.CourseOverGround10thDegrees;
            message.DimensionToBow = parser.DimensionToBow;
            message.DimensionToPort = parser.DimensionToPort;
            message.DimensionToStarboard = parser.DimensionToStarboard;
            message.DimensionToStern = parser.DimensionToStern;
            message.IsAssigned = parser.IsAssigned;
            message.IsDteNotReady = parser.IsDteNotReady;
            message.MessageType = checked((int)parser.MessageType);
            message.PositionAccuracy = parser.PositionAccuracy;
            message.PositionFixType = parser.PositionFixType;
            message.RaimFlag = parser.RaimFlag;
            message.RegionalReserved139 = parser.RegionalReserved139;
            message.RegionalReserved38 = parser.RegionalReserved38;
            message.RepeatIndicator = parser.RepeatIndicator;
            message.ShipName = shipNameAscii.ParseVesselName();
            message.ShipType = parser.ShipType;
            message.Spare308 = parser.Spare308;
            message.SpeedOverGroundTenths = parser.SpeedOverGroundTenths;
            message.TimeStampSecond = parser.TimeStampSecond;
            message.TrueHeadingDegrees = parser.TrueHeadingDegrees;
            message.Mmsi = parser.Mmsi;
            message.Position = new Position
            {
                Latitude = parser.Latitude10000thMins.From10000thMinsToDegrees(),
                Longitude = parser.Longitude10000thMins.From10000thMinsToDegrees()
            };

            this.telemetry.OnNext(message);
        }

        private void ParseMessageType24(ReadOnlySpan<byte> asciiPayload, uint padding)
        {

            var part = NmeaAisStaticDataReportParser.GetPartNumber(asciiPayload, padding);

            if (part == 0)
            {
                var message = new AisMessageType24Part0 { MessageType = 24 };
                var parser = new NmeaAisStaticDataReportParserPartA(asciiPayload, padding);
                Span<byte> vesselNameAscii = stackalloc byte[(int) parser.VesselName.CharacterCount];
                parser.VesselName.WriteAsAscii(vesselNameAscii);

                message.MessageType = checked((int)parser.MessageType);
                message.PartNumber = parser.PartNumber;
                message.RepeatIndicator = parser.RepeatIndicator;
                message.Spare160 = parser.Spare160;
                message.Mmsi = parser.Mmsi;

                this.telemetry.OnNext(message);
                return;
            }

            if (part == 1)
            {
                var message = new AisMessageType24Part1 { MessageType = 24 };
                var parser = new NmeaAisStaticDataReportParserPartB(asciiPayload, padding);
                Span<byte> callSignAscii = stackalloc byte[(int) parser.CallSign.CharacterCount];
                parser.CallSign.WriteAsAscii(callSignAscii);
                Span<byte> vendorIdRev3Ascii = stackalloc byte[(int) parser.VendorIdRev3.CharacterCount];
                parser.VendorIdRev3.WriteAsAscii(vendorIdRev3Ascii);
                Span<byte> vendorIdRev4Ascii = stackalloc byte[(int) parser.VendorIdRev4.CharacterCount];
                parser.VendorIdRev3.WriteAsAscii(vendorIdRev4Ascii);

                message.CallSign = callSignAscii.ParseVesselName();
                message.DimensionToBow = parser.DimensionToBow;
                message.DimensionToPort = parser.DimensionToPort;
                message.DimensionToStarboard = parser.DimensionToStarboard;
                message.DimensionToStern = parser.DimensionToStern;
                message.MessageType = checked((int)parser.MessageType);
                message.Mmsi = parser.Mmsi;
                message.MothershipMmsi = parser.MothershipMmsi;
                message.PartNumber = parser.PartNumber;
                message.RepeatIndicator = parser.RepeatIndicator;
                message.SerialNumber = parser.SerialNumber;
                message.ShipType = parser.ShipType;
                message.Spare162 = parser.Spare162;
                message.UnitModelCode = parser.UnitModelCode;
                message.VendorIdRev3 = vendorIdRev3Ascii.ParseVesselName();
                message.VendorIdRev4 = vendorIdRev4Ascii.ParseVesselName();

                this.telemetry.OnNext(message);
            }
        }

        public void OnError(in ReadOnlySpan<byte> line, Exception error, int lineNumber)
        {
            throw new NotImplementedException();
        }

        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void Progress(
            bool done,
            int totalNmeaLines,
            int totalAisMessages,
            int totalTicks,
            int nmeaLinesSinceLastUpdate,
            int aisMessagesSinceLastUpdate,
            int ticksSinceLastUpdate)
        {
            throw new NotImplementedException();
        }
    }
}