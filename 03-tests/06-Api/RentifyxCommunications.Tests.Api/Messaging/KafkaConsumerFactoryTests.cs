using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using RentifyxCommunications.Api.Messaging;
using RentifyxCommunications.Application.Abstractions;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Messaging;

public sealed class KafkaConsumerFactoryTests
{
    private static IOptions<KafkaOptions> KafkaOptions() => Options.Create(new KafkaOptions("test-group"));

    [Fact]
    public void Create_WithNoBootstrapServersConfigured_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        KafkaConsumerFactory sut = new(configuration, KafkaOptions(), environment.Object);

        Action act = () => sut.Create("main");

        act.Should().Throw<InvalidOperationException>().WithMessage("*kafka*");
    }

    [Fact]
    public void Create_WhenNotProduction_ReturnsPlaintextConsumer()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kafka"] = "localhost:9092" })
            .Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        KafkaConsumerFactory sut = new(configuration, KafkaOptions(), environment.Object);

        using IConsumer<Ignore, string> consumer = sut.Create("main");

        consumer.Should().NotBeNull();
    }

    [Fact]
    public void Create_WhenProductionWithoutAwsRegionConfigured_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kafka"] = "broker:9098" })
            .Build();
        Mock<IHostEnvironment> environment = new();
        environment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        KafkaConsumerFactory sut = new(configuration, KafkaOptions(), environment.Object);

        Action act = () => sut.Create("main");

        act.Should().Throw<InvalidOperationException>().WithMessage("*AWS:Region*");
    }

    [Fact]
    public void Create_WhenProductionWithAwsRegionConfigured_ReturnsSaslIamConsumer()
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
        KafkaConsumerFactory sut = new(configuration, KafkaOptions(), environment.Object);

        using IConsumer<Ignore, string> consumer = sut.Create("main");

        consumer.Should().NotBeNull();
    }
}
