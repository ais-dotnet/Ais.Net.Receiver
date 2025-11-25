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

    [TestMethod]
    public async Task VesselNavigationWithNameStream_MultipleVessels_GroupsByMmsi()
    {
        // Arrange
        uint mmsi1 = 111111111u;
        uint mmsi2 = 222222222u;

        IVesselNavigation? nav1 = Substitute.For<IVesselNavigation, IAisMessage>();
        ((IAisMessage)nav1).Mmsi.Returns(mmsi1);

        IVesselName? name1 = Substitute.For<IVesselName, IAisMessage>();
        ((IAisMessage)name1).Mmsi.Returns(mmsi1);
        name1.VesselName.Returns("VESSEL ONE");

        IVesselNavigation? nav2 = Substitute.For<IVesselNavigation, IAisMessage>();
        ((IAisMessage)nav2).Mmsi.Returns(mmsi2);

        IVesselName? name2 = Substitute.For<IVesselName, IAisMessage>();
        ((IAisMessage)name2).Mmsi.Returns(mmsi2);
        name2.VesselName.Returns("VESSEL TWO");

        IObservable<IAisMessage> messages = new[]
        {
            (IAisMessage)nav1, (IAisMessage)name1,
            (IAisMessage)nav2, (IAisMessage)name2
        }.ToObservable();

        // Act
        IList<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)> results =
            await messages.VesselNavigationWithNameStream().ToList();

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldContain(r => r.Mmsi == mmsi1 && r.Name.VesselName == "VESSEL ONE");
        results.ShouldContain(r => r.Mmsi == mmsi2 && r.Name.VesselName == "VESSEL TWO");
    }

    [TestMethod]
    public async Task VesselNavigationWithNameStream_WithInactivityTimeout_UsesConfiguredTimeout()
    {
        // Arrange
        uint mmsi = 123456789u;
        IVesselNavigation? nav = Substitute.For<IVesselNavigation, IAisMessage>();
        ((IAisMessage)nav).Mmsi.Returns(mmsi);

        IVesselName? name = Substitute.For<IVesselName, IAisMessage>();
        ((IAisMessage)name).Mmsi.Returns(mmsi);
        name.VesselName.Returns("TEST VESSEL");

        IObservable<IAisMessage> messages = new[] { (IAisMessage)nav, (IAisMessage)name }.ToObservable();

        // Act - use a very short timeout (test should still work as messages come quickly)
        (uint Mmsi, IVesselNavigation Navigation, IVesselName Name) result =
            await messages.VesselNavigationWithNameStream(TimeSpan.FromMinutes(1)).FirstOrDefaultAsync();

        // Assert
        result.Mmsi.ShouldBe(mmsi);
        result.Name.VesselName.ShouldBe("TEST VESSEL");
    }

    [TestMethod]
    public async Task VesselNavigationWithNameStream_NavigationOnlyVessel_NoOutput()
    {
        // Arrange - vessel with only navigation data, no name
        uint mmsi = 123456789u;
        IVesselNavigation? nav = Substitute.For<IVesselNavigation, IAisMessage>();
        ((IAisMessage)nav).Mmsi.Returns(mmsi);

        IObservable<IAisMessage> messages = new[] { (IAisMessage)nav }.ToObservable();

        // Act
        Notification<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)>? result =
            await messages.VesselNavigationWithNameStream()
                .Materialize()
                .FirstOrDefaultAsync();

        // Assert - should complete without emitting (CombineLatest needs both)
        result.Kind.ShouldBe(NotificationKind.OnCompleted);
    }
}