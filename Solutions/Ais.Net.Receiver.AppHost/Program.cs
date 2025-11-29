IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// builder.AddProject<Projects.Ais_Net_Receiver_Host_Console>("console");
builder.AddProject<Projects.Ais_Net_Receiver_Host_Worker>("worker");

builder.Build().Run();
