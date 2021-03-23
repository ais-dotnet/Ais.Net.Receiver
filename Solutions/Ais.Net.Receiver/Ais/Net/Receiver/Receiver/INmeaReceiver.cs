// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
    using System.Collections.Generic;
    using System.Threading;

    public interface INmeaReceiver
    {
        IAsyncEnumerable<string> GetAsync(CancellationToken cancellationToken = default);
    }
}