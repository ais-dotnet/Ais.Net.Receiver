namespace Ais.Net.Receiver
{
    using System;
    using System.Reactive.Subjects;
    using System.Text;

    public class NmeaToAisTelemetryStreamProcessor : INmeaAisMessageStreamProcessor
    {
        private readonly Subject<AisTelmetry> telemetry = new();

        public IObservable<AisTelmetry> Telemetry => this.telemetry;

        public void OnNext(in NmeaLineParser parsedLine, in ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            var aisTelemetry = new AisTelmetry();
            int messageType = NmeaPayloadParser.PeekMessageType(asciiPayload, padding);

            try
            {
                switch (messageType)
                {
                    case >= 1 and <= 3:
                    {
                        var parser = new NmeaAisPositionReportClassAParser(asciiPayload, padding);

                        aisTelemetry.Mmsi = parser.Mmsi;
                        aisTelemetry.Position = new LonLat
                        {
                            Latitude = Convert.ToDouble(parser.Latitude10000thMins / 600000.0),
                            Longitude = Convert.ToDouble(parser.Longitude10000thMins / 600000.0)
                        };

                        this.telemetry.OnNext(aisTelemetry);
                        return;
                    }
                    case 18:
                    {
                        var parser = new NmeaAisPositionReportClassBParser(asciiPayload, padding);

                        aisTelemetry.Mmsi = parser.Mmsi;
                        aisTelemetry.Position = new LonLat
                        {
                            Latitude = Convert.ToDouble(parser.Latitude10000thMins / 600000.0),
                            Longitude = Convert.ToDouble(parser.Longitude10000thMins / 600000.0)
                        };

                        this.telemetry.OnNext(aisTelemetry);
                        return;
                    }
                    case 19:
                    {
                        var parser = new NmeaAisPositionReportExtendedClassBParser(asciiPayload, padding);
                        Span<byte> vesselNameAscii = stackalloc byte[(int) parser.ShipName.CharacterCount];
                        parser.ShipName.WriteAsAscii(vesselNameAscii);

                        aisTelemetry.VesselName = Encoding.ASCII.GetString(vesselNameAscii);
                        aisTelemetry.Mmsi = parser.Mmsi;
                        aisTelemetry.Position = new LonLat
                        {
                            Latitude = Convert.ToDouble(parser.Latitude10000thMins / 600000.0),
                            Longitude = Convert.ToDouble(parser.Longitude10000thMins / 600000.0)
                        };

                        this.telemetry.OnNext(aisTelemetry);
                        return;
                    }
                    case 24:
                    {
                        var part = NmeaAisStaticDataReportParser.GetPartNumber(asciiPayload, padding);

                        if (part == 0)
                        {
                            var parser = new NmeaAisStaticDataReportParserPartA(asciiPayload, padding);
                            Span<byte> vesselNameAscii = stackalloc byte[(int) parser.VesselName.CharacterCount];
                            parser.VesselName.WriteAsAscii(vesselNameAscii);

                            aisTelemetry.VesselName = Encoding.ASCII.GetString(vesselNameAscii);
                            aisTelemetry.Mmsi = parser.Mmsi;

                            this.telemetry.OnNext(aisTelemetry);
                            return;
                        }

                        if (part != 0)
                        {
                            var parser = new NmeaAisStaticDataReportParserPartB(asciiPayload, padding);

                            aisTelemetry.Mmsi = parser.Mmsi;

                            this.telemetry.OnNext(aisTelemetry);
                        }

                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
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