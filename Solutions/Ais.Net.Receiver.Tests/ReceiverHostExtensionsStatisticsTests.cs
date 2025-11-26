using Ais.Net.Receiver.Receiver;
using Microsoft.Reactive.Testing;
using NSubstitute;
using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ReceiverHostExtensionsStatisticsTests
{
    [TestMethod]
    public async Task GetStreamStatistics_WithMessages_ReturnsNonZeroCounts()
    {
        // Arrange
        TestScheduler scheduler = new();
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        // Yield 5 messages
        List<ReadOnlyMemory<byte>> messages = Enumerable.Repeat((ReadOnlyMemory<byte>)bytes, 5).ToList();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(messages.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);

        List<(long Message, long Sentence, long Error)> stats = [];
        // Use a small period to ensure we get a buffer
        using IDisposable sub = host.GetStreamStatistics(TimeSpan.FromSeconds(1), scheduler).Subscribe(stats.Add);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Buffer emits on completion, so we don't strictly need to advance time if the stream completes.
        // But to be safe and explicit about the scheduler usage:
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1.1).Ticks);

        // Assert
        // We expect some stats
        stats.ShouldNotBeEmpty();

        // Check total messages counted
        long totalMessages = stats.Sum(s => s.Message);
        long totalSentences = stats.Sum(s => s.Sentence);
        long totalErrors = stats.Sum(s => s.Error);

        // 5 messages were yielded
        totalMessages.ShouldBe(5);
        totalSentences.ShouldBe(5);
        totalErrors.ShouldBe(0);
    }

    [TestMethod]
    public async Task GetStreamStatistics_WithGarbage_ReturnsErrorCounts()
    {
        // Arrange
        TestScheduler scheduler = new();
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "GARBAGE"u8.ToArray();

        // Yield 5 garbage messages
        List<ReadOnlyMemory<byte>> messages = Enumerable.Repeat((ReadOnlyMemory<byte>)bytes, 5).ToList();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(messages.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);

        List<(long Message, long Sentence, long Error)> stats = [];
        using IDisposable sub = host.GetStreamStatistics(TimeSpan.FromSeconds(1), scheduler).Subscribe(stats.Add);

        // Act
        await host.StartAsync(CancellationToken.None);
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1.1).Ticks);

        // Assert
        stats.ShouldNotBeEmpty();
        long totalErrors = stats.Sum(s => s.Error);
        long totalSentences = stats.Sum(s => s.Sentence);
        long totalMessages = stats.Sum(s => s.Message);

        totalErrors.ShouldBe(5);
        totalSentences.ShouldBe(5);
        totalMessages.ShouldBe(0);
    }

    [TestMethod]
    public async Task GetStreamStatistics_EmptyStream_ReturnsZeroCounts()
    {
        // Arrange
        TestScheduler scheduler = new();
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<ReadOnlyMemory<byte>>());

        ReceiverHost host = new(receiver);

        List<(long Message, long Sentence, long Error)> stats = [];
        using IDisposable sub = host.GetStreamStatistics(TimeSpan.FromSeconds(1), scheduler).Subscribe(stats.Add);

        // Act
        await host.StartAsync(CancellationToken.None);
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1.1).Ticks);

        // Assert
        long totalMessages = stats.Sum(s => s.Message);
        long totalSentences = stats.Sum(s => s.Sentence);
        long totalErrors = stats.Sum(s => s.Error);

        totalMessages.ShouldBe(0);
        totalSentences.ShouldBe(0);
        totalErrors.ShouldBe(0);
    }

    public TestContext TestContext { get; set; } = null!;
}