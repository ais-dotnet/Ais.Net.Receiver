using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class NetworkStreamNmeaReceiverReconnectionTests
{
    [TestMethod]
    public async Task GetAsync_AcrossReconnects_DisposesReaderEachCycleAndKeepsYielding()
    {
        CyclingStreamReader reader = new(linesPerConnection: 3);
        NetworkStreamNmeaReceiver receiver = new(
            reader, "localhost", 12345, TimeProvider.System, retryPeriodicity: TimeSpan.FromMilliseconds(1));

        using CancellationTokenSource cts = new();
        int lineCount = 0;

        // Each connection yields 3 lines then ends, forcing a reconnect. Consume across several cycles.
        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync(cts.Token))
        {
            lineCount++;
            if (lineCount >= 9)
            {
                await cts.CancelAsync();
            }
        }

        lineCount.ShouldBeGreaterThanOrEqualTo(9);
        reader.ConnectCount.ShouldBeGreaterThanOrEqualTo(3);

        // Every connection that was established was disposed - no reader/socket leaks across reconnects.
        reader.DisposeCount.ShouldBeGreaterThanOrEqualTo(reader.ConnectCount);

        await receiver.DisposeAsync();
    }

    [TestMethod]
    public async Task GetAsync_ReportsConnectionStateOnConnectAndDisconnect()
    {
        CyclingStreamReader reader = new(linesPerConnection: 2);
        List<bool> states = [];

        NetworkStreamNmeaReceiver receiver = new(
            reader, "localhost", 12345, TimeProvider.System,
            retryPeriodicity: TimeSpan.FromMilliseconds(1),
            onConnectionStateChanged: state =>
            {
                lock (states)
                {
                    states.Add(state);
                }
            });

        using CancellationTokenSource cts = new();
        int lineCount = 0;

        await foreach (ReadOnlyMemory<byte> line in receiver.GetAsync(cts.Token))
        {
            if (++lineCount >= 4)
            {
                await cts.CancelAsync();
            }
        }

        await receiver.DisposeAsync();

        // Each connection reports connected (true) then, when it ends, disconnected (false) - so the
        // health monitor tracks the real socket rather than the host's lifetime.
        states.ShouldContain(true);
        states.ShouldContain(false);
    }

    private sealed class CyclingStreamReader : INmeaStreamReader
    {
        private static readonly byte[] Sentence = "!AIVDM,1,1,,A,13u?etPv2;0n:dDPwUM1U1Cb069D,0*24"u8.ToArray();
        private readonly int linesPerConnection;
        private int linesRemaining;

        public CyclingStreamReader(int linesPerConnection) => this.linesPerConnection = linesPerConnection;

        public int ConnectCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool Connected { get; private set; }

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            this.ConnectCount++;
            this.linesRemaining = this.linesPerConnection;
            this.Connected = true;
            return Task.CompletedTask;
        }

        public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (this.linesRemaining-- > 0)
            {
                return new ValueTask<ReadOnlyMemory<byte>?>(Sentence);
            }

            this.Connected = false;
            return new ValueTask<ReadOnlyMemory<byte>?>(result: null); // end of stream -> reconnect
        }

        public ValueTask DisposeAsync()
        {
            this.DisposeCount++;
            this.Connected = false;
            return ValueTask.CompletedTask;
        }
    }
}
