using RentifyxCommunications.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.RentifyxCommunications_Api>("clean-arch-api")
    .WithHttpHealthCheck("/health")
    .WithScalar();

await builder.Build().RunAsync();
