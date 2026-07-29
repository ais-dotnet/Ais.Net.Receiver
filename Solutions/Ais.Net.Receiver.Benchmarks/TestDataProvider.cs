namespace Ais.Net.Receiver.Benchmarks;

public record NmeaTestCase(string Name, byte[] Data)
{
    public override string ToString() => Name;
}

public static class TestDataProvider
{
    public static IEnumerable<NmeaTestCase> NmeaSentencesWithTags()
    {
        yield return new NmeaTestCase("With Tags 1", @"\s:1000001,c:1637760000*24\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8.ToArray());
        yield return new NmeaTestCase("With Tags 2", @"\s:1000001,c:1637760001*24\!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray());
        yield return new NmeaTestCase("With Tags 3", @"\g:1-2-1234,s:1000001,c:1637760002*XX\!AIVDM,2,1,,A,55P5TL01VIaAL@7WKO4806<D18E8222222222216C8888888888888888800,0*33"u8.ToArray());
    }

    public static IEnumerable<NmeaTestCase> NmeaSentencesWithoutTags()
    {
        yield return new NmeaTestCase("Without Tags 1", "!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C"u8.ToArray());
        yield return new NmeaTestCase("Without Tags 2", "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray());
        yield return new NmeaTestCase("Without Tags 3", "!AIVDM,2,1,,A,55P5TL01VIaAL@7WKO4806<D18E8222222222216C8888888888888888800,0*33"u8.ToArray());
        yield return new NmeaTestCase("Without Tags 4", "!AIVDM,2,2,,A,00000000000,2*25"u8.ToArray());
    }
}