extern alias AppHostRef;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RentifyxCommunications.Tests.Integration;

public sealed class AppHostTests
{
    [Fact]
    public async Task AppHost_StartsApiResource_AndHealthEndpointRespondsHealthy()
    {
        IDistributedApplicationTestingBuilder appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostRef::Projects.RentifyxCommunications_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler());

        await using DistributedApplication app = await appHost.BuildAsync();
        ResourceNotificationService resourceNotificationService =
            app.Services.GetRequiredService<ResourceNotificationService>();

        await app.StartAsync();

        await resourceNotificationService
            .WaitForResourceAsync("rentifyx-communications-api", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60));

        using HttpClient httpClient = app.CreateHttpClient("rentifyx-communications-api");
        using HttpResponseMessage response = await httpClient.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task AppHost_StartsKafkaResource_AndBrokerIsReachable()
    {
        IDistributedApplicationTestingBuilder appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostRef::Projects.RentifyxCommunications_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler());

        await using DistributedApplication app = await appHost.BuildAsync();
        ResourceNotificationService resourceNotificationService =
            app.Services.GetRequiredService<ResourceNotificationService>();

        await app.StartAsync();

        await resourceNotificationService
            .WaitForResourceAsync("kafka", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60));

        string? connectionString = await app.GetConnectionStringAsync("kafka");

        connectionString.Should().NotBeNullOrWhiteSpace();

        ProducerBuilder<Null, string> producerBuilder = new(new ProducerConfig { BootstrapServers = connectionString });
        using IProducer<Null, string> producer = producerBuilder.Build();
        DeliveryResult<Null, string> deliveryResult = await producer.ProduceAsync(
            "aspire-smoke-test",
            new Message<Null, string> { Value = "ping" });

        deliveryResult.Status.Should().Be(PersistenceStatus.Persisted);
    }
}
