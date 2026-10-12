// <copyright file="FakeNmeaBroadcaster.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Text;

namespace Ais.Net.Broadcaster.Tests;

/// <summary>
/// An in-memory <see cref="INmeaBroadcaster"/> that records every call, in order, so tests can
/// assert on what a pipeline did without opening any sockets.
/// </summary>
internal sealed class FakeNmeaBroadcaster(string name = "fake://sink") : INmeaBroadcaster
{
    private readonly Lock gate = new();
    private readonly List<string> events = [];
    private readonly List<byte[]> broadcasts = [];

    /// <summary>Gets the recorded calls, in order: <c>start</c>, <c>broadcast</c>, <c>disconnect</c>, <c>stop</c>.</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (this.gate)
            {
                return [.. this.events];
            }
        }
    }

    /// <summary>Gets copies of every broadcast line.</summary>
    public IReadOnlyList<byte[]> Broadcasts
    {
        get
        {
            lock (this.gate)
            {
                return [.. this.broadcasts];
            }
        }
    }

    /// <summary>Gets every broadcast line decoded as ASCII.</summary>
    public IReadOnlyList<string> BroadcastText => [.. this.Broadcasts.Select(b => Encoding.ASCII.GetString(b))];

    public int BroadcastCount
    {
        get
        {
            lock (this.gate)
            {
                return this.broadcasts.Count;
            }
        }
    }

    public int StartCount => this.Count("start");

    public int StopCount => this.Count("stop");

    public int DisconnectCount => this.Count("disconnect");

    public string Name => name;

    public int ClientCount
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.Record("start");
        return Task.CompletedTask;
    }

    public ValueTask BroadcastAsync(ReadOnlyMemory<byte> sentence, CancellationToken cancellationToken)
    {
        // Per the contract the sink copies, as the caller's buffer is only valid for this call.
        lock (this.gate)
        {
            this.events.Add("broadcast");
            this.broadcasts.Add(sentence.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    public Task DisconnectAllAsync(CancellationToken cancellationToken)
    {
        this.Record("disconnect");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        this.Record("stop");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Record(string evt)
    {
        lock (this.gate)
        {
            this.events.Add(evt);
        }
    }

    private int Count(string evt)
    {
        lock (this.gate)
        {
            return this.events.Count(e => e == evt);
        }
    }
}
