// <copyright file="AisConfig.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

// Configuration binding types are typically better off as null-oblivious, because the contents
// of config files are outside the compiler's control.
#nullable disable annotations

namespace Ais.Net.Receiver.Configuration
{
    using System;

    public class AisConfig
    {
        public string Host { get; set; }

        public int Port { get; set; }

        public int RetryAttempts { get; set; }

        public TimeSpan RetryPeriodicity { get; set; }
    }
}