// <copyright file="IStorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ais.Net.Receiver.Storage;

public interface IStorageClient
{
    Task PersistAsync(IEnumerable<string> messages);
}