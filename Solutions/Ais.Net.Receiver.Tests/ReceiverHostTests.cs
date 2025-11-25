using System.Text;

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
        string message = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
        byte[] bytes = Encoding.ASCII.GetBytes(message);
            
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);
        IAisMessage? receivedMessage = null;
        host.Messages.Subscribe(msg => receivedMessage = msg);

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        receivedMessage.ShouldNotBeNull();
        receivedMessage.ShouldBeAssignableTo<IVesselIdentity>();
        ((IVesselIdentity)receivedMessage).Mmsi.ShouldBe(265547250u);
    }

    [TestMethod]
    public async Task StartAsync_ValidMessage_PublishesSentence()
    {
        // Arrange
        INmeaReceiver? receiver = Substitute.For<INmeaReceiver>();
        string message = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24";
        byte[] bytes = Encoding.ASCII.GetBytes(message);
            
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);
        string? receivedSentence = null;
        host.Sentences.Subscribe(s => receivedSentence = s);

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
        byte[] bytes = Encoding.ASCII.GetBytes(message);
            
        receiver.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { (ReadOnlyMemory<byte>)bytes }.ToAsyncEnumerable());

        ReceiverHost host = new(receiver);
        (Exception Exception, string Line)? receivedError = null;
        host.Errors.Subscribe(e => receivedError = e);
        // Must subscribe to Messages or Metadata to trigger processing
        host.Messages.Subscribe(_ => { });

        // Act
        await host.StartAsync(CancellationToken.None);

        // Assert
        receivedError.ShouldNotBeNull();
        receivedError.Value.Line.ShouldBe(message);
        receivedError.Value.Exception.ShouldBeOfType<ArgumentException>();
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