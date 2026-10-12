// <copyright file="WebSocketTestServer.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Net.WebSockets;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Tests;

/// <summary>
/// A real, in-process WebSocket server (Kestrel on a loopback ephemeral port) for exercising the
/// WebSocket receiver end to end. Every accepted connection is handed to the supplied handler.
/// </summary>
internal sealed class WebSocketTestServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly ConcurrentDictionary<WebSocket, byte> sockets;

    private WebSocketTestServer(WebApplication app, ConcurrentDictionary<WebSocket, byte> sockets, Uri uri)
    {
        this.app = app;
        this.sockets = sockets;
        this.Uri = uri;
    }

    /// <summary>
    /// Gets the <c>ws://127.0.0.1:port/</c> address of the server.
    /// </summary>
    public Uri Uri { get; }

    public static async Task<WebSocketTestServer> StartAsync(Func<WebSocket, Task> handler, CancellationToken cancellationToken)
    {
        ConcurrentDictionary<WebSocket, byte> sockets = new();

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
            sockets.TryAdd(socket, 0);
            try
            {
                await handler(socket);
            }
            catch
            {
                // The client went away or the server is shutting down; nothing to report.
            }
            finally
            {
                sockets.TryRemove(socket, out _);
            }
        });

        await app.StartAsync(cancellationToken);

        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Uri httpUri = new(address);
        Uri uri = new($"ws://127.0.0.1:{httpUri.Port}/");

        return new WebSocketTestServer(app, sockets, uri);
    }

    /// <summary>
    /// Reads from the socket until the client closes it (or the connection fails), replying to a close.
    /// </summary>
    public static async Task WaitForCloseAsync(WebSocket socket)
    {
        byte[] buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Abort any connection a handler is still holding open, so shutdown does not wait on it.
        foreach (WebSocket socket in this.sockets.Keys)
        {
            socket.Abort();
        }

        await this.app.StopAsync();
        await this.app.DisposeAsync();
    }
}
