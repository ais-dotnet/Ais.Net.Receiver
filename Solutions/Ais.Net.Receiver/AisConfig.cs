namespace Ais.Net.Receiver
{
    using System;

    public class AisConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public int RetryAttempts { get; set; }
        public TimeSpan RetryPeriodicity { get; set; }
    }

    public class StorageConfig
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
        public int WriteBatchSize { get; set; }
    }
}