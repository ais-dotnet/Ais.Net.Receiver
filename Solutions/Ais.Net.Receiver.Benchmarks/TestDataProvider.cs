using System.Text;

public static class TestDataProvider
{
    public static IEnumerable<byte[]> NmeaSentencesWithTags()
    {
        yield return Encoding.ASCII.GetBytes(@"\s:1000001,c:1637760000*24\!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C");
        yield return Encoding.ASCII.GetBytes(@"\s:1000001,c:1637760001*24\!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24");
        yield return Encoding.ASCII.GetBytes(@"\g:1-2-1234,s:1000001,c:1637760002*XX\!AIVDM,2,1,,A,55P5TL01VIaAL@7WKO4806<D18E8222222222216C8888888888888888800,0*33");
    }

    public static IEnumerable<byte[]> NmeaSentencesWithoutTags()
    {
        yield return Encoding.ASCII.GetBytes("!AIVDM,1,1,,B,177KQJ5000G?tO`K>RA1wUbN0TKH,0*5C");
        yield return Encoding.ASCII.GetBytes("!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24");
        yield return Encoding.ASCII.GetBytes("!AIVDM,2,1,,A,55P5TL01VIaAL@7WKO4806<D18E8222222222216C8888888888888888800,0*33");
        yield return Encoding.ASCII.GetBytes("!AIVDM,2,2,,A,00000000000,2*25");
    }
}
