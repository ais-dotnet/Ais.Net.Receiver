// <copyright file="IStorageClient.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Storage.Azure.Blob
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IStorageClient
    {
        Task PersistAsync(IEnumerable<string> messages);
    }
}