// <copyright file="IStorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Storage;

public interface IStorageClient
{
    Task PersistAsync(IEnumerable<string> messages);
}