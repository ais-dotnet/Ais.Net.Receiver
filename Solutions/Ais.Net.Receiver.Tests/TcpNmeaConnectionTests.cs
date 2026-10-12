// <copyright file="TcpNmeaConnectionTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class TcpNmeaConnectionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Properties_DescribeALiveTcpEndpoint()
    {
        TcpNmeaConnection connection = new("127.0.0.1", 5631);

        connection.Kind.ShouldBe(NmeaSourceKind.Live);
        connection.Transport.ShouldBe("tcp");
        connection.ProtocolName.ShouldBeNull();
        connection.EndpointAddress.ShouldBe("127.0.0.1");
        connection.EndpointPort.ShouldBe(5631);
        connection.Connected.ShouldBeFalse();
    }

    [TestMethod]
    public void Constructor_RejectsInvalidArguments()
    {
        Should.Throw<ArgumentException>(() => new TcpNmeaConnection(" ", 5631));
        Should.Throw<ArgumentOutOfRangeException>(() => new TcpNmeaConnection("localhost", 0));
        Should.Throw<ArgumentOutOfRangeException>(() => new TcpNmeaConnection("localhost", 65536));
    }

    [TestMethod]
    public async Task ConnectAsync_ConnectsToListenerAndReportsConnected()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        using TcpListener listener = StartListener(out int port);
        await using TcpNmeaConnection connection = new("127.0.0.1", port);

        await connection.ConnectAsync(cts.Token);

        connection.Connected.ShouldBeTrue();
    }

    [TestMethod]
    public async Task ReadLineAsync_ReadsTwoLines()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        using TcpListener listener = StartListener(out int port);
        await using TcpNmeaConnection connection = new("127.0.0.1", port);

        await connection.ConnectAsync(cts.Token);
        using TcpClient serverClient = await listener.AcceptTcpClientAsync(cts.Token);
        await serverClient.GetStream().WriteAsync("Line1\nLine2\r\n"u8.ToArray(), cts.Token);

        // The returned buffer is only valid until the next read, so verify each before the next.
        (await ReadStringAsync(connection, cts.Token)).ShouldBe("Line1");
        (await ReadStringAsync(connection, cts.Token)).ShouldBe("Line2");
    }

    [TestMethod]
    public async Task ReadLineAsync_FinalUnterminatedLineAtServerClose_IsReturnedThenNull()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        using TcpListener listener = StartListener(out int port);
        await using TcpNmeaConnection connection = new("127.0.0.1", port);

        await connection.ConnectAsync(cts.Token);
        TcpClient serverClient = await listener.AcceptTcpClientAsync(cts.Token);
        NetworkStream stream = serverClient.GetStream();
        await stream.WriteAsync("!AIVDM,1,1,,A,tail,0*00"u8.ToArray(), cts.Token);
        await stream.FlushAsync(cts.Token);
        serverClient.Close();

        (await ReadStringAsync(connection, cts.Token)).ShouldBe("!AIVDM,1,1,,A,tail,0*00");
        (await connection.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ReadLineAsync_ServerDisconnectsMidStream_ReturnsDataThenNull()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        using TcpListener listener = StartListener(out int port);
        await using TcpNmeaConnection connection = new("127.0.0.1", port);

        await connection.ConnectAsync(cts.Token);
        TcpClient serverClient = await listener.AcceptTcpClientAsync(cts.Token);
        NetworkStream stream = serverClient.GetStream();
        await stream.WriteAsync("CompleteLine\n"u8.ToArray(), cts.Token);
        await stream.FlushAsync(cts.Token);
        serverClient.Close();

        (await ReadStringAsync(connection, cts.Token)).ShouldBe("CompleteLine");
        (await connection.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ConnectAsync_ClosedPort_ThrowsSocketException()
    {
        using CancellationTokenSource cts = this.CreateTimeout();

        // Take a free port, then release it so nothing is listening there.
        int port;
        using (TcpListener listener = StartListener(out port))
        {
            listener.Stop();
        }

        await using TcpNmeaConnection connection = new("127.0.0.1", port);

        await Should.ThrowAsync<SocketException>(connection.ConnectAsync(cts.Token));
        connection.Connected.ShouldBeFalse();
        (await connection.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ConnectAsync_Cancelled_ThrowsOperationCanceledExceptionAndStaysDisconnected()
    {
        using TcpListener listener = StartListener(out int port);
        await using TcpNmeaConnection connection = new("127.0.0.1", port);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(connection.ConnectAsync(cancelled.Token));

        connection.Connected.ShouldBeFalse();
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledTwice()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        using TcpListener listener = StartListener(out int port);
        TcpNmeaConnection connection = new("127.0.0.1", port);
        await connection.ConnectAsync(cts.Token);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        connection.Connected.ShouldBeFalse();
        (await connection.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ConnectAsync_AfterServerClose_ReconnectsThroughSameInstance()
    {
        using CancellationTokenSource cts = this.CreateTimeout();
        using TcpListener listener = StartListener(out int port);
        await using TcpNmeaConnection connection = new("127.0.0.1", port);

        // First session: read a line, then the server closes.
        await connection.ConnectAsync(cts.Token);
        TcpClient first = await listener.AcceptTcpClientAsync(cts.Token);
        await first.GetStream().WriteAsync("First\n"u8.ToArray(), cts.Token);
        first.Close();

        (await ReadStringAsync(connection, cts.Token)).ShouldBe("First");
        (await connection.ReadLineAsync(cts.Token)).HasValue.ShouldBeFalse();

        // Second session through the same instance.
        await connection.ConnectAsync(cts.Token);
        connection.Connected.ShouldBeTrue();
        using TcpClient second = await listener.AcceptTcpClientAsync(cts.Token);
        await second.GetStream().WriteAsync("Second\n"u8.ToArray(), cts.Token);

        (await ReadStringAsync(connection, cts.Token)).ShouldBe("Second");
    }

    private static TcpListener StartListener(out int port)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static async Task<string> ReadStringAsync(TcpNmeaConnection connection, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte>? line = await connection.ReadLineAsync(cancellationToken);
        line.HasValue.ShouldBeTrue();
        return Encoding.ASCII.GetString(line.Value.Span);
    }

    private CancellationTokenSource CreateTimeout()
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(this.TestContext.CancellationToken);
        cts.CancelAfter(TestTimeout);
        return cts;
    }
}
