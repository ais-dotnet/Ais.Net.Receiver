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