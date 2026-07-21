using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using RentifyxCommunications.Infrastructure.Messaging;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class KafkaProducerFactoryTests
{
    [Fact]
    public void Create_WithNoBootstrapServersConfigured_Throws()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        KafkaProducerFactory sut = new(configuration);

        Action act = () => sut.Create();

        act.Should().Throw<InvalidOperationException>().WithMessage("*kafka*");
    }

    [Fact]
    public void Create_WhenConfigured_ReturnsPlaintextProducer()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kafka"] = "localhost:9092" })
            .Build();
        KafkaProducerFactory sut = new(configuration);

        using IProducer<Null, string> producer = sut.Create();

        producer.Should().NotBeNull();
    }
}
