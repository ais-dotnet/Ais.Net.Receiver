// <copyright file="NmeaToAisTelemetryStreamProcessor.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

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
                    case 5:
                    {
                        this.ParseMessageType5(asciiPayload, padding);
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
                // Console.WriteLine($"Unknown type: {messageType}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{messageType}] {e.Message}");
            }
        }

        private void ParseMessageTypes1Through3(ReadOnlySpan<byte> asciiPayload, uint padding, int messageType)
        {
            var parser = new NmeaAisPositionReportClassAParser(asciiPayload, padding);

            var message = new AisMessageType1Through3(
                MessageType: messageType,
                Mmsi: parser.Mmsi,
                CourseOverGround10thDegrees: parser.CourseOverGround10thDegrees,
                ManoeuvreIndicator: parser.ManoeuvreIndicator,
                NavigationStatus: parser.NavigationStatus,
                PositionAccuracy: parser.PositionAccuracy,
                RadioSlotTimeout: parser.RadioSlotTimeout,
                RadioSubMessage: parser.RadioSubMessage,
                RadioSyncState: parser.RadioSyncState,
                RaimFlag: parser.RaimFlag,
                RateOfTurn: parser.RateOfTurn,
                RepeatIndicator: parser.RepeatIndicator,
                SpareBits145: parser.SpareBits145,
                SpeedOverGroundTenths: parser.SpeedOverGroundTenths,
                TimeStampSecond: parser.TimeStampSecond,
                TrueHeadingDegrees: parser.TrueHeadingDegrees,
                Position: Position.From10000thMins(parser.Latitude10000thMins, parser.Longitude10000thMins)
            );

            this.telemetry.OnNext(message);
        }
        private void ParseMessageType5(ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            var parser = new NmeaAisStaticAndVoyageRelatedDataParser(asciiPayload, padding);
            var message = new AisMessageType5(
                Mmsi: parser.Mmsi,
                ImoNumber: parser.ImoNumber,
                CallSign:  parser.CallSign.TextFieldToString(),
                Destination: parser.Destination.TextFieldToString(),
                VesselName: parser.VesselName.TextFieldToString(),
                ShipType: parser.ShipType,
                DimensionToBow: parser.DimensionToBow,
                DimensionToPort: parser.DimensionToPort,
                DimensionToStarboard: parser.DimensionToStarboard,
                DimensionToStern: parser.DimensionToStern,
                Draught10thMetres: parser.Draught10thMetres,
                PositionFixType: parser.PositionFixType
                );

            this.telemetry.OnNext(message);
        }

        private void ParseMessageType18(ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            var parser = new NmeaAisPositionReportClassBParser(asciiPayload, padding);
            var message = new AisMessageType18(
                Mmsi: parser.Mmsi,
                Position: Position.From10000thMins(parser.Latitude10000thMins, parser.Longitude10000thMins),
                CanAcceptMessage22ChannelAssignment: parser.CanAcceptMessage22ChannelAssignment,
                CanSwitchBands: parser.CanSwitchBands,
                CsUnit: parser.CsUnit,
                HasDisplay: parser.HasDisplay,
                IsDscAttached: parser.IsDscAttached,
                RadioStatusType: parser.RadioStatusType,
                RegionalReserved139: parser.RegionalReserved139,
                RegionalReserved38: parser.RegionalReserved38,
                CourseOverGround10thDegrees: parser.CourseOverGround10thDegrees,
                PositionAccuracy: parser.PositionAccuracy,
                SpeedOverGroundTenths: parser.SpeedOverGroundTenths,
                TimeStampSecond: parser.TimeStampSecond,
                TrueHeadingDegrees: parser.TrueHeadingDegrees,
                IsAssigned: parser.IsAssigned,
                RaimFlag: parser.RaimFlag,
                RepeatIndicator: parser.RepeatIndicator);

            this.telemetry.OnNext(message);
        }

        private void ParseMessageType19(ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            var parser = new NmeaAisPositionReportExtendedClassBParser(asciiPayload, padding);
            Span<byte> shipNameAscii = stackalloc byte[(int) parser.ShipName.CharacterCount];
            parser.ShipName.WriteAsAscii(shipNameAscii);

            var message = new AisMessageType19(
                Mmsi: parser.Mmsi,
                ShipName: shipNameAscii.GetString(),
                CourseOverGround10thDegrees: parser.CourseOverGround10thDegrees,
                DimensionToBow: parser.DimensionToBow,
                DimensionToPort: parser.DimensionToPort,
                DimensionToStarboard: parser.DimensionToStarboard,
                DimensionToStern: parser.DimensionToStern,
                IsAssigned: parser.IsAssigned,
                IsDteNotReady: parser.IsDteNotReady,
                PositionAccuracy: parser.PositionAccuracy,
                PositionFixType: parser.PositionFixType,
                RaimFlag: parser.RaimFlag,
                RegionalReserved139: parser.RegionalReserved139,
                RegionalReserved38: parser.RegionalReserved38,
                RepeatIndicator: parser.RepeatIndicator,
                ShipType: parser.ShipType,
                Spare308: parser.Spare308,
                SpeedOverGroundTenths: parser.SpeedOverGroundTenths,
                TimeStampSecond: parser.TimeStampSecond,
                TrueHeadingDegrees: parser.TrueHeadingDegrees,
                Position: Position.From10000thMins(parser.Latitude10000thMins, parser.Longitude10000thMins)
            );

            this.telemetry.OnNext(message);
        }

        private void ParseMessageType24(ReadOnlySpan<byte> asciiPayload, uint padding)
        {

            var part = NmeaAisStaticDataReportParser.GetPartNumber(asciiPayload, padding);

            if (part == 0)
            {
                var parser = new NmeaAisStaticDataReportParserPartA(asciiPayload, padding);
                Span<byte> vesselNameAscii = stackalloc byte[(int) parser.VesselName.CharacterCount];
                parser.VesselName.WriteAsAscii(vesselNameAscii);

                var message = new AisMessageType24Part0(
                    Mmsi: parser.Mmsi,
                    PartNumber: parser.PartNumber,
                    RepeatIndicator: parser.RepeatIndicator,
                    Spare160: parser.Spare160
                );

                this.telemetry.OnNext(message);
                return;
            }

            if (part == 1)
            {
                var parser = new NmeaAisStaticDataReportParserPartB(asciiPayload, padding);
                
                Span<byte> callSignAscii = stackalloc byte[(int) parser.CallSign.CharacterCount];
                parser.CallSign.WriteAsAscii(callSignAscii);
                
                Span<byte> vendorIdRev3Ascii = stackalloc byte[(int) parser.VendorIdRev3.CharacterCount];
                parser.VendorIdRev3.WriteAsAscii(vendorIdRev3Ascii);
                
                Span<byte> vendorIdRev4Ascii = stackalloc byte[(int) parser.VendorIdRev4.CharacterCount];
                parser.VendorIdRev3.WriteAsAscii(vendorIdRev4Ascii);

                var message = new AisMessageType24Part1(
                    Mmsi: parser.Mmsi,
                    CallSign: callSignAscii.GetString(),
                    VendorIdRev3: vendorIdRev3Ascii.GetString(),
                    VendorIdRev4: vendorIdRev4Ascii.GetString())
                {
                    DimensionToBow = parser.DimensionToBow,
                    DimensionToPort = parser.DimensionToPort,
                    DimensionToStarboard = parser.DimensionToStarboard,
                    DimensionToStern = parser.DimensionToStern,
                    MothershipMmsi = parser.MothershipMmsi,
                    PartNumber = parser.PartNumber,
                    RepeatIndicator = parser.RepeatIndicator,
                    SerialNumber = parser.SerialNumber,
                    ShipType = parser.ShipType,
                    Spare162 = parser.Spare162,
                    UnitModelCode = parser.UnitModelCode
                };

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