using RentifyxCommunications.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.RentifyxCommunications_Api>("rentifyx-communications-api")
    .WithHttpHealthCheck("/health")
    .WithScalar();

await builder.Build().RunAsync();
