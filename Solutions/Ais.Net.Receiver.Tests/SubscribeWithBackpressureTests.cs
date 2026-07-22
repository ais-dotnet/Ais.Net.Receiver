using System.Reactive.Subjects;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class SubscribeWithBackpressureTests
{
    [TestMethod]
    public void SubscribeWithBackpressure_InvokesOnDropped_ForEachDeclinedItem()
    {
        Subject<int> source = new();
        int accepted = 0;
        int dropped = 0;

        // Consumer accepts even values and declines odd ones (as a full bounded block would).
        using IDisposable subscription = source.SubscribeWithBackpressure(
            value =>
            {
                if (value % 2 == 0)
                {
                    accepted++;
                    return true;
                }

                return false;
            },
            () => dropped++);

        for (int i = 0; i < 10; i++)
        {
            source.OnNext(i);
        }

        accepted.ShouldBe(5);
        dropped.ShouldBe(5);
    }

    [TestMethod]
    public void SubscribeWithBackpressure_NeverDrops_WhenConsumerAcceptsEverything()
    {
        Subject<int> source = new();
        int dropped = 0;

        using IDisposable subscription = source.SubscribeWithBackpressure(_ => true, () => dropped++);

        for (int i = 0; i < 100; i++)
        {
            source.OnNext(i);
        }

        dropped.ShouldBe(0);
    }
}
