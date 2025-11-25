using System.Reactive;
using System.Reactive.Linq;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Receiver;

using NSubstitute;
using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ReceiverHostExtensionsTests
{
    [TestMethod]
    public async Task VesselNavigationWithNameStream_CombinesNavigationAndName()
    {
        // Arrange
        uint mmsi = 123456789u;
        IVesselNavigation? navigationMessage = Substitute.For<IVesselNavigation, IAisMessage>();
        ((IAisMessage)navigationMessage).Mmsi.Returns(mmsi);
            
        IVesselName? nameMessage = Substitute.For<IVesselName, IAisMessage>();
        ((IAisMessage)nameMessage).Mmsi.Returns(mmsi);
        nameMessage.VesselName.Returns("TEST VESSEL");

        IObservable<IAisMessage> messages = new[] { (IAisMessage)navigationMessage, (IAisMessage)nameMessage }.ToObservable();

        // Act
        (uint Mmsi, IVesselNavigation Navigation, IVesselName Name) result = await messages.VesselNavigationWithNameStream().FirstOrDefaultAsync();

        // Assert
        result.Mmsi.ShouldBe(mmsi);
        result.Navigation.ShouldBe(navigationMessage);
        result.Name.ShouldBe(nameMessage);
        result.Name.VesselName.ShouldBe("TEST VESSEL");
    }

    [TestMethod]
    public async Task VesselNavigationWithNameStream_IgnoresUnmatchedMessages()
    {
        // Arrange
        uint mmsi1 = 111111111u;
        uint mmsi2 = 222222222u;
            
        IVesselNavigation? nav1 = Substitute.For<IVesselNavigation, IAisMessage>();
        ((IAisMessage)nav1).Mmsi.Returns(mmsi1);
            
        IVesselName? name2 = Substitute.For<IVesselName, IAisMessage>();
        ((IAisMessage)name2).Mmsi.Returns(mmsi2);

        IObservable<IAisMessage> messages = new[] { (IAisMessage)nav1, (IAisMessage)name2 }.ToObservable();

        // Act
        // Should not produce any result because MMSIs don't match
        // The stream should complete without emitting any items.
        Notification<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)>? result = await messages.VesselNavigationWithNameStream()
            .Materialize()
            .FirstOrDefaultAsync();

        // Assert
        // We expect OnCompleted because no items were emitted
        result.Kind.ShouldBe(NotificationKind.OnCompleted);
    }
}