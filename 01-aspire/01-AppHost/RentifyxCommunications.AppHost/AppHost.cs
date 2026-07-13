using RentifyxCommunications.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<KafkaServerResource> kafka = builder
    .AddKafka("kafka")
    .WithKafkaUI();

builder.AddProject<Projects.RentifyxCommunications_Api>("rentifyx-communications-api")
    .WithReference(kafka)
    .WithHttpHealthCheck("/health")
    .WithScalar();

await builder.Build().RunAsync();
