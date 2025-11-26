// <copyright file="ReceiverTelemetry.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics.Metrics;

namespace Ais.Net.Receiver.Receiver;

public class ReceiverTelemetry : IDisposable
{
    private readonly Meter meter;
    private readonly Counter<long> messagesReceived;
    private readonly Counter<long> sentencesReceived;
    private readonly Counter<long> errorsReceived;
    private readonly List<IDisposable> subscriptions = [];

    public ReceiverTelemetry(string meterName)
    {
        this.meter = new Meter(meterName);
        this.messagesReceived = this.meter.CreateCounter<long>("ais.messages.received");
        this.sentencesReceived = this.meter.CreateCounter<long>("ais.sentences.received");
        this.errorsReceived = this.meter.CreateCounter<long>("ais.errors.count");
    }

    public void Bind(ReceiverHost host)
    {
        this.subscriptions.Add(host.Messages.Subscribe(_ => this.messagesReceived.Add(1)));
        this.subscriptions.Add(host.Sentences.Subscribe(_ => this.sentencesReceived.Add(1)));
        this.subscriptions.Add(host.Errors.Subscribe(_ => this.errorsReceived.Add(1)));
    }

    public void Dispose()
    {
        foreach (IDisposable sub in this.subscriptions)
        {
            sub.Dispose();
        }
        
        this.meter.Dispose();
        GC.SuppressFinalize(this);
    }
}