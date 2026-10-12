// <copyright file="TcpClientNmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Receiver.Receiver;

/// <summary>
/// An <see cref="INmeaStreamReader"/> over a raw TCP feed. This is a thin adapter over
/// <see cref="TcpNmeaConnection"/>, kept for source compatibility: each
/// <see cref="ConnectAsync"/> creates a new connection to the given host and port.
/// </summary>
public class TcpClientNmeaStreamReader : INmeaStreamReader
{
    private readonly ILogger<TcpClientNmeaStreamReader>? logger;
    private TcpNmeaConnection? connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpClientNmeaStreamReader"/> class.
    /// </summary>
    /// <param name="logger">An optional logger, passed through to each <see cref="TcpNmeaConnection"/>.</param>
    public TcpClientNmeaStreamReader(ILogger<TcpClientNmeaStreamReader>? logger = null)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the reader currently holds an open TCP connection.
    /// </summary>
    public bool Connected => this.connection?.Connected == true;

    /// <inheritdoc/>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        await this.DisposeAsync().ConfigureAwait(false);

        this.connection = new TcpNmeaConnection(host, port, this.logger);
        await this.connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken) =>
        this.connection is { } c ? c.ReadLineAsync(cancellationToken) : ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

    /// <summary>
    /// Disposes the current connection, if any. Safe to call more than once.
    /// </summary>
    /// <returns>A task that completes when the connection has been released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (this.connection is { } c)
        {
            this.connection = null;
            await c.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
