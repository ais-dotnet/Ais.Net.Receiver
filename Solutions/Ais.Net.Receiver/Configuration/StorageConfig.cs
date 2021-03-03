namespace Ais.Net.Receiver.Configuration
{
    public class StorageConfig
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
        public int WriteBatchSize { get; set; }
    }
}