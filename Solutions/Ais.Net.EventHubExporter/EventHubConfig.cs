namespace Ais.Net.EventHubExporter
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class EventHubConfig
    {
        public string ConnectionString { get; set; }
        public string EventHubName { get; set; }
    }
}
