namespace Ais.Net.Receiver
{
    using System;
    using System.Text;

    public class ConsoleExporter : INmeaAisMessageStreamProcessor
    {
        public void OnNext(in NmeaLineParser parsedLine, in ReadOnlySpan<byte> asciiPayload, uint padding)
        {
            try
            {
                int messageType = NmeaPayloadParser.PeekMessageType(asciiPayload, parsedLine.Padding);

                if (messageType == 18)
                {
                    /*var parser = new NmeaAisPositionReportClassBParser(asciiPayload, parsedLine.Padding);
                    Span<byte> vesselNameAscii = stackalloc byte[(int) parser.ShipName.CharacterCount];
                    parser.ShipName.WriteAsAscii(vesselNameAscii);

                    Console.WriteLine(Encoding.ASCII.GetString(vesselNameAscii));*/
                }

                if (messageType == 19)
                {
                    var parser = new NmeaAisPositionReportExtendedClassBParser(asciiPayload, parsedLine.Padding);
                    Span<byte> vesselNameAscii = stackalloc byte[(int) parser.ShipName.CharacterCount];
                    parser.ShipName.WriteAsAscii(vesselNameAscii);

                    Console.WriteLine(Encoding.ASCII.GetString(vesselNameAscii));
                }

                if (messageType == 24)
                {
                    var part = NmeaAisStaticDataReportParser.GetPartNumber(asciiPayload, parsedLine.Padding);

                    if (part == 0)
                    {
                        var parser = new NmeaAisStaticDataReportParserPartA(asciiPayload, parsedLine.Padding);
                        Span<byte> vesselNameAscii = stackalloc byte[(int) parser.VesselName.CharacterCount];
                        parser.VesselName.WriteAsAscii(vesselNameAscii);

                        Console.WriteLine($"{Encoding.ASCII.GetString(vesselNameAscii).Trim('@').Trim()} [{parser.Mmsi}]");
                    }

                    if (part != 0)
                    {
                        /*var parser = new NmeaAisStaticDataReportParserPartB(asciiPayload, parsedLine.Padding);
                        Span<byte> vesselNameAscii = stackalloc byte[(int) parser..VesselName.CharacterCount];
                        parser.VesselName.WriteAsAscii(vesselNameAscii);

                        Console.WriteLine(Encoding.ASCII.GetString(vesselNameAscii));*/
                    }

                    // Console.WriteLine(part);
                }

                /*var parser = new NmeaAisPositionReportClassAParser(asciiPayload, parsedLine.Padding);
                Console.WriteLine($"{Convert.ToDouble(parser.Latitude10000thMins / 600000)}, {Convert.ToDouble(parser.Longitude10000thMins / 600000)}");*/
            }
            catch(Exception)
            {
                //Console.WriteLine(exception.Message);
            }
        }

        public void OnError(in ReadOnlySpan<byte> line, Exception error, int lineNumber)
        {
            Console.WriteLine(error.Message);
        }

        public void OnCompleted()
        {
        }

        public void Progress(bool done, int totalNmeaLines, int totalAisMessages, int totalTicks, int nmeaLinesSinceLastUpdate, int aisMessagesSinceLastUpdate, int ticksSinceLastUpdate)
        {
        }
    }
}