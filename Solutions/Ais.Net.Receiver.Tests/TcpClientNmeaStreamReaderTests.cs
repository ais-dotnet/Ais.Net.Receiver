using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ais.Net.Receiver.Receiver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Ais.Net.Receiver.Tests
{
    [TestClass]
    public class TcpClientNmeaStreamReaderTests
    {
        [TestMethod]
        public async Task ConnectAsync_ConnectsToListener()
        {
            // Arrange
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var reader = new TcpClientNmeaStreamReader();

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
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var reader = new TcpClientNmeaStreamReader();

            try
            {
                await reader.ConnectAsync("127.0.0.1", port, CancellationToken.None);
                
                // Accept client and send data
                var serverClient = await listener.AcceptTcpClientAsync();
                var stream = serverClient.GetStream();
                var data = Encoding.ASCII.GetBytes("Line1\nLine2\r\n");
                await stream.WriteAsync(data);

                // Act
                var line1 = await reader.ReadLineAsync(CancellationToken.None);
                var line2 = await reader.ReadLineAsync(CancellationToken.None);

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
}
