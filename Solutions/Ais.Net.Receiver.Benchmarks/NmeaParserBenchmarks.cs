using Ais.Net.Receiver.Parser;
using BenchmarkDotNet.Attributes;

namespace Ais.Net.Receiver.Benchmarks;

[MemoryDiagnoser]
public class NmeaParserBenchmarks
{
    [Benchmark]
    [ArgumentsSource(nameof(GetMessagesWithTags))]
    public (int StationId, long UnixTimestamp) ParseTags(NmeaTestCase testCase) => testCase.Data.ParseNmeaBlockTags();

    [Benchmark]
    [ArgumentsSource(nameof(GetMessagesWithoutTags))]
    public bool IsMissingTags(NmeaTestCase testCase) => testCase.Data.IsMissingNmeaBlockTags;

    [Benchmark]
    [ArgumentsSource(nameof(GetMessagesWithoutTags))]
    public ReadOnlyMemory<byte> PrependTags(NmeaTestCase testCase) => ((ReadOnlyMemory<byte>)testCase.Data).PrependNmeaBlockTags(TimeProvider.System);

    public static IEnumerable<NmeaTestCase> GetMessagesWithTags() => TestDataProvider.NmeaSentencesWithTags();

    public static IEnumerable<NmeaTestCase> GetMessagesWithoutTags() => TestDataProvider.NmeaSentencesWithoutTags();
}