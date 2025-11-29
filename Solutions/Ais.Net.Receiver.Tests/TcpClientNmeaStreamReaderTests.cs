using System.Net;
using System.Net.Sockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class TcpClientNmeaStreamReaderTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ConnectAsync_ConnectsToListener()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Act
            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            // Assert
            reader.Connected.ShouldBeTrue();
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_ReadsLinesFromStream()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            // Accept client and send data
            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);
            NetworkStream stream = serverClient.GetStream();
            byte[] data = "Line1\nLine2\r\n"u8.ToArray();
            await stream.WriteAsync(data, this.TestContext.CancellationTokenSource.Token);

            // Act
            ReadOnlyMemory<byte>? line1 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            ReadOnlyMemory<byte>? line2 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert
            line1.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line1.Value.Span).ShouldBe("Line1");

            line2.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line2.Value.Span).ShouldBe("Line2");
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_HandlesVariousLineEndings()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);
            NetworkStream stream = serverClient.GetStream();
            // Send data with CRLF line ending
            byte[] data = "Line1\r\nLine2\r\n"u8.ToArray();
            await stream.WriteAsync(data, this.TestContext.CancellationTokenSource.Token);

            // Act
            ReadOnlyMemory<byte>? line1 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            ReadOnlyMemory<byte>? line2 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert
            line1.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line1.Value.Span).ShouldBe("Line1");
            line2.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line2.Value.Span).ShouldBe("Line2");
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_StreamClosed_ReturnsNull()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);
            serverClient.Close(); // Close server side

            // Act - wait a bit for the close to propagate
            await Task.Delay(50, this.TestContext.CancellationTokenSource.Token);
            ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert
            line.HasValue.ShouldBeFalse();
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public void Connected_BeforeConnect_ReturnsFalse()
    {
        // Arrange
        TcpClientNmeaStreamReader reader = new();

        // Act & Assert
        reader.Connected.ShouldBeFalse();
    }

    [TestMethod]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        TcpClientNmeaStreamReader reader = new();

        // Act & Assert - should not throw
        await reader.DisposeAsync();
        await reader.DisposeAsync();
    }

    [TestMethod]
    public async Task ConnectAsync_ConnectionRefused_ThrowsSocketException()
    {
        // Arrange - use a port with no listener
        TcpClientNmeaStreamReader reader = new();

        try
        {
            // Act & Assert - connecting to a closed port should throw
            await Should.ThrowAsync<SocketException>(
                reader.ConnectAsync("127.0.0.1", 59999, CancellationToken.None));
        }
        finally
        {
            await reader.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ConnectAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        // Don't accept connections - let connect hang
        using CancellationTokenSource cts = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Cancel immediately
            await cts.CancelAsync();

            // Act & Assert
            await Should.ThrowAsync<OperationCanceledException>(
                reader.ConnectAsync("127.0.0.1", port, cts.Token));
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_ServerDisconnectsMidStream_ReturnsDataThenNull()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);
            NetworkStream stream = serverClient.GetStream();

            // Send one complete line, then disconnect
            byte[] data = "CompleteLine\n"u8.ToArray();
            await stream.WriteAsync(data, this.TestContext.CancellationTokenSource.Token);
            await stream.FlushAsync(this.TestContext.CancellationTokenSource.Token);
            serverClient.Close();

            // Act
            ReadOnlyMemory<byte>? line1 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            await Task.Delay(50, this.TestContext.CancellationTokenSource.Token); // Allow close to propagate
            ReadOnlyMemory<byte>? line2 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert
            line1.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line1.Value.Span).ShouldBe("CompleteLine");
            line2.HasValue.ShouldBeFalse(); // Null after disconnect
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_WhenServerSendsReset_ReturnsNullGracefully()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);

            // Configure socket to send RST instead of FIN on close
            serverClient.LingerState = new LingerOption(true, 0);
            serverClient.Close();

            // Act - wait for RST to propagate
            await Task.Delay(50, this.TestContext.CancellationTokenSource.Token);
            ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert - should handle gracefully by returning null
            line.HasValue.ShouldBeFalse();
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            // Accept but don't send any data
            _ = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);

            using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(50));

            // Act & Assert - reading with no data should timeout
            Exception ex = await Should.ThrowAsync<Exception>(
                reader.ReadLineAsync(cts.Token).AsTask());

            (ex is OperationCanceledException || ex is IOException).ShouldBeTrue($"Expected OperationCanceledException or IOException but got {ex.GetType().Name}");
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_WhenDataArrivesSlowly_ReadsSuccessfullyWithinTimeout()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);

            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);
            NetworkStream stream = serverClient.GetStream();

            // Send data with delay - but within timeout
            Task sendTask = Task.Run(async () =>
            {
                await Task.Delay(30, this.TestContext.CancellationTokenSource.Token);
                byte[] data = "SlowLine\n"u8.ToArray();
                await stream.WriteAsync(data, this.TestContext.CancellationTokenSource.Token);
                await stream.FlushAsync(this.TestContext.CancellationTokenSource.Token);
            });

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));

            // Act
            ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(cts.Token);

            // Ensure send task completed successfully
            await sendTask;

            // Assert - should receive data before timeout
            line.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line.Value.Span).ShouldBe("SlowLine");
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_AfterServerDisconnects_ReturnsNullToSignalEndOfStream()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);
            reader.Connected.ShouldBeTrue();

            TcpClient serverClient = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);
            serverClient.Close();

            // Wait for close to propagate
            await Task.Delay(50, this.TestContext.CancellationTokenSource.Token);

            // Act - the proper way to detect disconnection is via ReadLineAsync returning null
            ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert - ReadLineAsync returns null to indicate disconnection
            // Note: TcpClient.Connected property doesn't update until a failed I/O operation,
            // so we detect disconnection via the null return value from ReadLineAsync
            line.HasValue.ShouldBeFalse();
        }
        finally
        {
            await reader.DisposeAsync();
            listener?.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_AfterDispose_ReturnsNull()
    {
        // Arrange
        TcpListener? listener = null;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            await reader.ConnectAsync("127.0.0.1", port, this.TestContext.CancellationTokenSource.Token);
            _ = await listener.AcceptTcpClientAsync(this.TestContext.CancellationTokenSource.Token);

            // Dispose the reader
            await reader.DisposeAsync();

            // Act - reading after dispose should return null (reader is null)
            ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);

            // Assert
            line.HasValue.ShouldBeFalse();
        }
        finally
        {
            await reader.DisposeAsync();
            listener.Stop();
        }
    }
}