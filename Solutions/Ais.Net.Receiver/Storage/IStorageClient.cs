// <copyright file="Program.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Storage
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IStorageClient
    {
        Task PersistAsync(IEnumerable<string> messages);
    }
}