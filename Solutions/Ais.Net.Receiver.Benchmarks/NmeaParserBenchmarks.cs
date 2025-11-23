using Ais.Net.Receiver.Parser;
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class NmeaParserBenchmarks
{
    [Benchmark]
    [ArgumentsSource(nameof(GetMessagesWithTags))]
    public (int StationId, long UnixTimestamp) ParseTags(byte[] message) => message.ParseNmeaBlockTags();

    [Benchmark]
    [ArgumentsSource(nameof(GetMessagesWithoutTags))]
    public bool IsMissingTags(byte[] message) => message.IsMissingNmeaBlockTags();

    [Benchmark]
    [ArgumentsSource(nameof(GetMessagesWithoutTags))]
    public ReadOnlyMemory<byte> PrependTags(byte[] message) => NmeaMessageExtensions.PrependNmeaBlockTags(message);

    public IEnumerable<byte[]> GetMessagesWithTags() => TestDataProvider.NmeaSentencesWithTags();

    public IEnumerable<byte[]> GetMessagesWithoutTags() => TestDataProvider.NmeaSentencesWithoutTags();
}
