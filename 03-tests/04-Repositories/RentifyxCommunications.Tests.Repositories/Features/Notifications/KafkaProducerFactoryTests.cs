using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using RentifyxCommunications.Infrastructure.Messaging;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class KafkaProducerFactoryTests
{
    [Fact]
    public void Create_WithNoBootstrapServersConfigured_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        KafkaProducerFactory sut = new(configuration, environment.Object);

        Action act = () => sut.Create();

        act.Should().Throw<InvalidOperationException>().WithMessage("*kafka*");
    }

    [Fact]
    public void Create_WhenNotProduction_ReturnsPlaintextProducer()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kafka"] = "localhost:9092" })
            .Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        KafkaProducerFactory sut = new(configuration, environment.Object);

        using IProducer<Null, string> producer = sut.Create();

        producer.Should().NotBeNull();
    }

    [Fact]
    public void Create_WhenProductionWithoutAwsRegionConfigured_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kafka"] = "broker:9098" })
            .Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        KafkaProducerFactory sut = new(configuration, environment.Object);

        Action act = () => sut.Create();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AWS:Region*");
    }

    [Fact]
    public void Create_WhenProductionWithAwsRegionConfigured_ReturnsSaslIamProducer()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:kafka"] = "broker:9098",
                ["AWS:Region"] = "sa-east-1",
            })
            .Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        KafkaProducerFactory sut = new(configuration, environment.Object);

        using IProducer<Null, string> producer = sut.Create();

        producer.Should().NotBeNull();
    }
}
