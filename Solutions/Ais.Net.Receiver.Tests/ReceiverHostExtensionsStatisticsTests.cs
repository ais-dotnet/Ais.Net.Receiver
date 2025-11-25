using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Ais.Net.Models.Abstractions;
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
        string message = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(message);
            
        // Yield 5 messages
        List<ReadOnlyMemory<byte>> messages = [];
        for(int i=0; i<5; i++) messages.Add(bytes);
            
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(messages.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);
            
        List<(long Message, long Sentence, long Error)> stats = [];
        // Use a small period to ensure we get a buffer
        using IDisposable sub = host.GetStreamStatistics(TimeSpan.FromSeconds(1), scheduler).Subscribe(s => stats.Add(s));

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

        // 5 messages were yielded
        totalMessages.ShouldBe(5);
    }

    public TestContext TestContext { get; set; } = null!;
}