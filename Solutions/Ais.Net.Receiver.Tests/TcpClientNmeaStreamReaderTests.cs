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
            listener?.Stop();
            await reader.DisposeAsync();
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

            // Act & Assert - the returned buffer is only valid until the next read, so read and
            // verify each line before requesting the next.
            ReadOnlyMemory<byte>? line1 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            line1.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line1.Value.Span).ShouldBe("Line1");

            ReadOnlyMemory<byte>? line2 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            line2.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line2.Value.Span).ShouldBe("Line2");
        }
        finally
        {
            listener?.Stop();
            await reader.DisposeAsync();
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

            // Act & Assert - the returned buffer is only valid until the next read, so read and
            // verify each line before requesting the next.
            ReadOnlyMemory<byte>? line1 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            line1.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line1.Value.Span).ShouldBe("Line1");

            ReadOnlyMemory<byte>? line2 = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            line2.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line2.Value.Span).ShouldBe("Line2");
        }
        finally
        {
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
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
    public async Task ConnectAsync_WhenConnectionFails_CleansUpResourcesAndRemainsDisconnected()
    {
        // Arrange - use a port with no listener to trigger connection failure
        TcpClientNmeaStreamReader reader = new();

        // Act - attempting to connect to a closed port should fail and clean up
        await Should.ThrowAsync<SocketException>(
            reader.ConnectAsync("127.0.0.1", 59999, CancellationToken.None));

        // Assert - after failed connection, reader should be in clean state
        reader.Connected.ShouldBeFalse();

        // Should be able to dispose without issues (resources were cleaned up in catch block)
        await reader.DisposeAsync();

        // Should still be able to read (returns null since not connected)
        ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(CancellationToken.None);
        line.HasValue.ShouldBeFalse();
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
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_FinalLineWithoutTrailingNewline_IsReturnedBeforeNull()
    {
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

            // Send a sentence with NO trailing newline, then close the connection.
            await stream.WriteAsync("!AIVDM,1,1,,A,tail,0*00"u8.ToArray(), this.TestContext.CancellationTokenSource.Token);
            await stream.FlushAsync(this.TestContext.CancellationTokenSource.Token);
            serverClient.Close();

            // The final unterminated line is still emitted (StreamReader did), then end-of-stream.
            ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            line.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line.Value.Span).ShouldBe("!AIVDM,1,1,,A,tail,0*00");

            ReadOnlyMemory<byte>? next = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
            next.HasValue.ShouldBeFalse();
        }
        finally
        {
            listener?.Stop();
            await reader.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_OverLongLineWithoutNewline_IsBoundedAndResyncsToNextLine()
    {
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

            // A 32 KB burst with no newline (far over the reader's cap), then a clean sentence.
            byte[] junk = new byte[32 * 1024];
            Array.Fill(junk, (byte)'X');
            await stream.WriteAsync(junk, this.TestContext.CancellationTokenSource.Token);
            await stream.WriteAsync("\n!AIVDM,1,1,,A,good,0*00\n"u8.ToArray(), this.TestContext.CancellationTokenSource.Token);
            await stream.FlushAsync(this.TestContext.CancellationTokenSource.Token);

            // The reader must never surface a line longer than its cap, and must resync to the
            // clean sentence rather than buffering the junk without bound.
            string? recovered = null;
            for (int i = 0; i < 40 && recovered is null; i++)
            {
                ReadOnlyMemory<byte>? line = await reader.ReadLineAsync(this.TestContext.CancellationTokenSource.Token);
                if (line is null)
                {
                    break;
                }

                line.Value.Length.ShouldBeLessThanOrEqualTo(8192);

                if (Encoding.ASCII.GetString(line.Value.Span) == "!AIVDM,1,1,,A,good,0*00")
                {
                    recovered = "!AIVDM,1,1,,A,good,0*00";
                }
            }

            recovered.ShouldBe("!AIVDM,1,1,,A,good,0*00");
        }
        finally
        {
            listener?.Stop();
            await reader.DisposeAsync();
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
            listener?.Stop();
            await reader.DisposeAsync();
        }
    }
}