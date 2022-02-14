namespace Ais.Net.EventHubExporter
{
    using System;

    using Ais.Net.Receiver.Configuration;

    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    internal static class Program
    {
        private static void Main(string[] args)
        {
            // Default generic host will pick up arguments of the form:
            //  Azure:EventHub:ConnectionString=foobar Azure:EventHub:EventHubName=spong
            //if (args.Length != 1)
            //{
            //    Console.Error.WriteLine($"Usage: {typeof(Program).Assembly.GetName().Name} <event hub connection string>")
            //}
            //Console.WriteLine("Hello World!");

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContextBuilder, serviceCollection) =>
                {
                    serviceCollection.AddSingleton<IHostedService, NmeaToEventHubTransferService>();

                    serviceCollection
                        .AddOptions<EventHubConfig>()
                        .Bind(hostContextBuilder.Configuration.GetSection("Azure:EventHub"))
                        .Validate((EventHubConfig c, ILogger<EventHubConfig> logger) =>
                        {
                            if (string.IsNullOrWhiteSpace(c.ConnectionString) ||
                                string.IsNullOrWhiteSpace(c.EventHubName))
                            {
                                logger.LogError("Azure:EventHub:ConnectionString and Azure:EventHub:EventHubName configuration settings required - set either in configuration file, environment variables, or as command line arguments");
                                return false;
                            }

                            return true;
                        });
                    serviceCollection
                    .Configure<EventHubConfig>(hostContextBuilder.Configuration.GetSection("Azure:EventHub"));
                    serviceCollection.Configure<AisConfig>(hostContextBuilder.Configuration.GetSection("Ais"));
                });

    }
}
