namespace Endjin.Ais.Receiver
{
    using Microsoft.Azure.Storage;
    using Microsoft.Azure.Storage.Blob;
    using Microsoft.Extensions.Configuration;

    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reactive.Linq;
    using System.Text;

    public class StorageClient
    {
        private CloudBlobContainer container;
        private IConfiguration configuration;

        public StorageClient(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public void AppendMessages(IList<string> messages)
        {
            CloudAppendBlob appendBlob;

            try
            {
                appendBlob = GetAppendBlob();
            }
            catch
            {
                this.InitialiseConnection();

                appendBlob = GetAppendBlob();
            }

            appendBlob.AppendText(messages.Aggregate(new StringBuilder(), (sb, a) => sb.AppendLine(String.Join(",", a)), sb => sb.ToString()));
        }

        public void InitialiseConnection()
        {
            CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(this.configuration["connectionString"]);
            CloudBlobClient client = cloudStorageAccount.CreateCloudBlobClient();

            this.container = client.GetContainerReference(this.configuration["containerName"]);
            this.container.CreateIfNotExists();
        }

        private CloudAppendBlob GetAppendBlob()
        {
            CloudAppendBlob blob = this.container.GetAppendBlobReference($"{DateTimeOffset.Now.ToString("yyyyMMdd")}/{DateTimeOffset.Now.ToString("yyyyMMddTHH")}.nm4");

            if (!blob.Exists())
            {
                blob.CreateOrReplace();
            }

            return blob;
        }
    }
}