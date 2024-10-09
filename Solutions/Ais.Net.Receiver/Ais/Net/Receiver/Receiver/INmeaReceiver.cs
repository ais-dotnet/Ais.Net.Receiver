// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Threading;

namespace Ais.Net.Receiver.Receiver;

public interface INmeaReceiver
{
    IAsyncEnumerable<string> GetAsync(CancellationToken cancellationToken = default);
}