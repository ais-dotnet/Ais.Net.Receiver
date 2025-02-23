// <copyright file="INmeaStreamReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Abstracts network stream reading operations for NMEA messages
/// </summary>
public interface INmeaStreamReader : IAsyncDisposable
{
    /// <summary>
    /// Establishes a connection to the specified host and port
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken);
    
    /// <summary>
    /// Reads a line of text asynchronously
    /// </summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Gets whether data is available to be read
    /// </summary>
    bool DataAvailable { get; }
    
    /// <summary>
    /// Gets whether the connection is established
    /// </summary>
    bool Connected { get; }
}