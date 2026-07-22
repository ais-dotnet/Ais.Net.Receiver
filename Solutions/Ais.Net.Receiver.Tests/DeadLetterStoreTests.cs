using System.Text;

using Ais.Net.Receiver.Storage;

using Microsoft.Extensions.Time.Testing;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class DeadLetterStoreTests
{
    [TestMethod]
    public async Task WriteThenRead_RoundTripsMessages()
    {
        using TempDirectory dir = new();
        DeadLetterStore store = new(dir.Path, new FakeTimeProvider());

        List<ReadOnlyMemory<byte>> batch =
        [
            "!AIVDM,1,1,,A,aaaa,0*00"u8.ToArray(),
            "!AIVDM,1,1,,A,bbbb,0*00"u8.ToArray(),
        ];

        string file = await store.WriteAsync(batch);

        // The atomic write leaves exactly one visible ".nm4" file (no leftover ".nm4.tmp").
        store.GetPendingFiles().ShouldHaveSingleItem().ShouldBe(file);
        Directory.GetFiles(dir.Path).Length.ShouldBe(1);

        IReadOnlyList<ReadOnlyMemory<byte>> read = await DeadLetterStore.ReadBatchAsync(file);
        read.Count.ShouldBe(2);
        Encoding.ASCII.GetString(read[0].Span).ShouldBe("!AIVDM,1,1,,A,aaaa,0*00");
        Encoding.ASCII.GetString(read[1].Span).ShouldBe("!AIVDM,1,1,,A,bbbb,0*00");

        DeadLetterStore.Remove(file);
        store.GetPendingFiles().ShouldBeEmpty();
    }

    [TestMethod]
    public void GetPendingFiles_MissingDirectory_ReturnsEmpty()
    {
        using TempDirectory dir = new();
        DeadLetterStore store = new(Path.Combine(dir.Path, "not-created-yet"), new FakeTimeProvider());

        store.GetPendingFiles().ShouldBeEmpty();
    }

    [TestMethod]
    public async Task GetPendingFiles_ExcludesPartialTempFiles()
    {
        using TempDirectory dir = new();
        DeadLetterStore store = new(dir.Path, new FakeTimeProvider());

        // A real, fully written batch...
        string good = await store.WriteAsync(["GOOD"u8.ToArray()]);

        // ...alongside a leftover staging file from a hypothetical interrupted write.
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "deadletter-partial.nm4.tmp"), "half-written");

        store.GetPendingFiles().ShouldHaveSingleItem().ShouldBe(good);
    }

    [TestMethod]
    public async Task GetPendingFiles_ReturnsChronologicalOrder()
    {
        using TempDirectory dir = new();
        FakeTimeProvider time = new();
        DeadLetterStore store = new(dir.Path, time);

        string first = await store.WriteAsync(["FIRST"u8.ToArray()]);
        time.Advance(TimeSpan.FromSeconds(1));
        string second = await store.WriteAsync(["SECOND"u8.ToArray()]);

        store.GetPendingFiles().ShouldBe([first, second]);
    }
}
