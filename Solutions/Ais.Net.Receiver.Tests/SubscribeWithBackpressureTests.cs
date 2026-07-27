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

    [TestMethod]
    public void SubscribeWithBackpressure_ReportsSourceFaultAndCompletion_WhenHandlersSupplied()
    {
        Subject<int> faulting = new();
        Subject<int> completing = new();
        Exception? faulted = null;
        bool completed = false;
        IOException failure = new("stream lost");

        using IDisposable faultingSubscription = faulting.SubscribeWithBackpressure(
            _ => true, static () => { }, onError: ex => faulted = ex, onCompleted: static () => { });
        using IDisposable completingSubscription = completing.SubscribeWithBackpressure(
            _ => true, static () => { }, onError: static _ => { }, onCompleted: () => completed = true);

        faulting.OnError(failure);
        completing.OnCompleted();

        faulted.ShouldBeSameAs(failure);
        completed.ShouldBeTrue();
    }

    [TestMethod]
    public void SubscribeWithBackpressure_KeepsRxDefault_WhenNoErrorHandlerSupplied()
    {
        Subject<int> source = new();

        using IDisposable subscription = source.SubscribeWithBackpressure(_ => true, static () => { });

        // Without an explicit handler the fault must still surface rather than being swallowed: Rx
        // rethrows it on the producer's thread.
        Should.Throw<IOException>(() => source.OnError(new IOException("stream lost")));
    }
}
