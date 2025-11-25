using System.Net;
using System.Net.Sockets;
using System.Text;

using Ais.Net.Receiver.Receiver;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class TcpClientNmeaStreamReaderTests
{
    [TestMethod]
    public async Task ConnectAsync_ConnectsToListener()
    {
        // Arrange
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            // Act
            await reader.ConnectAsync("127.0.0.1", port, CancellationToken.None);

            // Assert
            reader.Connected.ShouldBeTrue();
        }
        finally
        {
            await reader.DisposeAsync();
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task ReadLineAsync_ReadsLinesFromStream()
    {
        // Arrange
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        TcpClientNmeaStreamReader reader = new();

        try
        {
            await reader.ConnectAsync("127.0.0.1", port, CancellationToken.None);
                
            // Accept client and send data
            TcpClient serverClient = await listener.AcceptTcpClientAsync();
            NetworkStream stream = serverClient.GetStream();
            byte[] data = Encoding.ASCII.GetBytes("Line1\nLine2\r\n");
            await stream.WriteAsync(data);

            // Act
            ReadOnlyMemory<byte>? line1 = await reader.ReadLineAsync(CancellationToken.None);
            ReadOnlyMemory<byte>? line2 = await reader.ReadLineAsync(CancellationToken.None);

            // Assert
            line1.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line1.Value.Span).ShouldBe("Line1");
                
            line2.HasValue.ShouldBeTrue();
            Encoding.ASCII.GetString(line2.Value.Span).ShouldBe("Line2");
        }
        finally
        {
            await reader.DisposeAsync();
            listener.Stop();
        }
    }
}