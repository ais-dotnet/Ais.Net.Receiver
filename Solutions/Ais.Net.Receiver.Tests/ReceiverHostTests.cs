using System.Runtime.CompilerServices;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Receiver;

using NSubstitute;
using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ReceiverHostTests
{
    [TestMethod]
    public async Task StartAsync_ValidMessage_PublishesMessage()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        IAisMessage? receivedMessage = null;
        using IDisposable subscription = host.Messages.Subscribe(msg => receivedMessage = msg);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();
        receivedMessage.Mmsi.ShouldBe(265547250u);
    }

    [TestMethod]
    public async Task StartAsync_ValidMessage_PublishesSentence()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        string message = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        string? receivedSentence = null;
        using IDisposable subscription = host.Sentences.Subscribe(s => receivedSentence = s);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        receivedSentence.ShouldNotBeNull();
        receivedSentence.ShouldContain(message);
    }


    [TestMethod]
    public async Task StartAsync_MalformedMessage_PublishesError()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        // "GARBAGE" causes NmeaLineParser to throw ArgumentException
        string message = "GARBAGE";
        byte[] bytes = "GARBAGE"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        (Exception Exception, string Line)? receivedError = null;
        using IDisposable errorSubscription = host.Errors.Subscribe(e => receivedError = e);
        // Must subscribe to Messages or Metadata to trigger processing
        using IDisposable messageSubscription = host.Messages.Subscribe(_ => { });

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        receivedError.ShouldNotBeNull();
        receivedError.Value.Line.ShouldBe(message);
        receivedError.Value.Exception.ShouldBeOfType<ArgumentException>();
    }

    [TestMethod]
    public async Task StartAsync_WhenCancellationRequestedAfterMessages_StopsProcessing()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        using CancellationTokenSource cts = new();
        int messageCount = 0;

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo => GenerateMessagesWithCancellation(callInfo.Arg<CancellationToken>()));

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        using IDisposable subscription = host.Messages.Subscribe(_ =>
        {
            messageCount++;
            if (messageCount >= 2)
            {
                cts.Cancel();
            }
        });

        // Act & Assert - should not throw, just stop
        await host.StartAsync(cts.Token);
        messageCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [TestMethod]
    public async Task StartAsync_MultipleSubscribers_AllReceiveMessages()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        IAisMessage? receivedMessage1 = null;
        IAisMessage? receivedMessage2 = null;
        using IDisposable sub1 = host.Messages.Subscribe(msg => receivedMessage1 = msg);
        using IDisposable sub2 = host.Messages.Subscribe(msg => receivedMessage2 = msg);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert - both subscribers should receive the same message
        receivedMessage1.ShouldNotBeNull();
        receivedMessage2.ShouldNotBeNull();
        receivedMessage1.Mmsi.ShouldBe(receivedMessage2.Mmsi);
    }

    [TestMethod]
    public async Task StartAsync_WhenStreamCompletes_CompletesAllObservables()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        bool messagesCompleted = false;
        bool sentencesCompleted = false;
        bool errorsCompleted = false;

        using IDisposable msgSub = host.Messages.Subscribe(
            onNext: _ => { },
            onCompleted: () => messagesCompleted = true);
        using IDisposable sentSub = host.Sentences.Subscribe(
            onNext: _ => { },
            onCompleted: () => sentencesCompleted = true);
        using IDisposable errSub = host.Errors.Subscribe(
            onNext: _ => { },
            onCompleted: () => errorsCompleted = true);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        messagesCompleted.ShouldBeTrue();
        sentencesCompleted.ShouldBeTrue();
        errorsCompleted.ShouldBeTrue();
    }

    [TestMethod]
    public async Task StartAsync_WhenMessageLacksBlockTags_PrependsTimestampTags()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        // Message without NMEA block tags (no leading \s: or \c: prefix)
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        string? receivedSentence = null;
        using IDisposable subscription = host.Sentences.Subscribe(s => receivedSentence = s);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert - sentence should have block tags prepended
        // Format: \s:<stationId>,c:<timestamp>*<checksum>\<original message>
        receivedSentence.ShouldNotBeNull();
        receivedSentence.ShouldStartWith("\\s:");
        receivedSentence.ShouldContain(",c:");
    }

    [TestMethod]
    public async Task StartAsync_WithMetadataSubscriber_PublishesMetadata()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        Metadata? receivedMetadata = null;
        using IDisposable subscription = host.Metadata.Subscribe(m => receivedMetadata = m);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        receivedMetadata.ShouldNotBeNull();
        receivedMetadata.Message.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task DisposeAsync_DisposesReceiver()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver, IAsyncDisposable>();
        ReceiverHost host = new(receiver, TimeProvider.System);

        // Act
        await host.DisposeAsync();

        // Assert
        await ((IAsyncDisposable)receiver).Received(1).DisposeAsync();
    }

    [TestMethod]
    public async Task StartAsync_RetriesOnFailure()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        // First call throws, second call returns messages
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new Exception("Connection failed"),
                _ => new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        await using ReceiverHost host = new(receiver, TimeProvider.System, TimeSpan.FromMilliseconds(1));
        int messageCount = 0;
        using IDisposable subscription = host.Messages.Subscribe(_ => messageCount++);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        messageCount.ShouldBe(1);
        // Verify GetAsync was called twice
        _ = receiver.Received(2).GetAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StartAsync_WhenCancelledDuringContinuousStream_ExitsGracefully()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        
        // Infinite stream
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(x => GenerateMessagesWithCancellation(x.Arg<CancellationToken>()));

        await using ReceiverHost host = new(receiver, TimeProvider.System);
        int messageCount = 0;
        using IDisposable subscription = host.Messages.Subscribe(_ => messageCount++);

        using CancellationTokenSource cts = new();
        cts.CancelAfter(100); // Cancel after a short time

        // Act
        try
        {
            await host.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        messageCount.ShouldBeGreaterThan(0);
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> GenerateMessagesWithCancellation(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] bytes = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return bytes;
            await Task.Yield();
        }
    }
}

// Helper to create IAsyncEnumerable from IEnumerable
public static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (T item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}