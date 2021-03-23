// <copyright file="NmeaReceiver.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Receiver
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;

    public class FileStreamNmeaReceiver : INmeaReceiver
    {
        private readonly string path;

        public FileStreamNmeaReceiver(string path)
        {
            this.path = path;
        }

        public int RetryAttemptLimit { get; }

        public TimeSpan RetryPeriodicity { get; }

        public async IAsyncEnumerable<string> GetAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var sr = new StreamReader(this.path);

            while (sr.Peek() >= 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                string? line = await sr.ReadLineAsync().ConfigureAwait(false);

                if (line is not null) { yield return line; }
            }
        }
    }
}