// Configuration binding types are typically better off as null-oblivious, because the contents
// of config files are outside the compiler's control.
#nullable disable annotations

namespace Ais.Net.Receiver.Configuration
{

    public class StorageConfig
    {
        public string ConnectionString { get; set; }
        public string ContainerName { get; set; }
        public int WriteBatchSize { get; set; }
    }
}