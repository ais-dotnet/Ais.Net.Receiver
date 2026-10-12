using System.Reactive.Subjects;

using Ais.Net.Models.Abstractions;
using Ais.Net.Receiver.Receiver;

using Microsoft.Reactive.Testing;

using NSubstitute;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class VesselNavigationWithNameStreamTests
{
    [TestMethod]
    public void DisposesVesselGroupAfterInactivity()
    {
        TestScheduler scheduler = new();
        Subject<IAisMessage> messages = new();
        List<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)> outputs = [];

        using IDisposable subscription = messages
            .VesselNavigationWithNameStream(TimeSpan.FromSeconds(10), scheduler)
            .Subscribe(outputs.Add);

        // Vessel 1 reports a name and a position -> one combined tuple.
        messages.OnNext(NameMessage(1, "ALPHA"));
        messages.OnNext(NavigationMessage(1));
        outputs.Count.ShouldBe(1);

        // Idle past the inactivity timeout -> the group for vessel 1 (and its retained name) is disposed.
        scheduler.AdvanceBy(TimeSpan.FromSeconds(11).Ticks);

        // A fresh navigation for vessel 1 with no new name. If the old group had leaked, its retained
        // name would combine with this navigation and emit again; a disposed group emits nothing.
        messages.OnNext(NavigationMessage(1));

        outputs.Count.ShouldBe(1);
    }

    [TestMethod]
    public void ReformsGroupAndEmitsAgainAfterInactivity()
    {
        TestScheduler scheduler = new();
        Subject<IAisMessage> messages = new();
        List<(uint Mmsi, IVesselNavigation Navigation, IVesselName Name)> outputs = [];

        using IDisposable subscription = messages
            .VesselNavigationWithNameStream(TimeSpan.FromSeconds(10), scheduler)
            .Subscribe(outputs.Add);

        messages.OnNext(NameMessage(1, "ALPHA"));
        messages.OnNext(NavigationMessage(1));
        outputs.Count.ShouldBe(1);

        scheduler.AdvanceBy(TimeSpan.FromSeconds(11).Ticks);

        // After inactivity, a full new report (name + navigation) forms a fresh group and emits again.
        messages.OnNext(NameMessage(1, "ALPHA"));
        messages.OnNext(NavigationMessage(1));
        outputs.Count.ShouldBe(2);
    }

    private static IAisMessage NavigationMessage(uint mmsi)
    {
        IAisMessage message = Substitute.For<IAisMessage, IVesselNavigation>();
        message.Mmsi.Returns(mmsi);
        return message;
    }

    private static IAisMessage NameMessage(uint mmsi, string name)
    {
        IAisMessage message = Substitute.For<IAisMessage, IVesselName>();
        message.Mmsi.Returns(mmsi);
        ((IVesselName)message).VesselName.Returns(name);
        return message;
    }
}
