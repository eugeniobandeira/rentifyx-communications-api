using Confluent.Kafka;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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
        KafkaConsumerFactory sut = new(configuration, KafkaOptions());

        Action act = () => sut.Create("main");

        act.Should().Throw<InvalidOperationException>().WithMessage("*kafka*");
    }

    [Fact]
    public void Create_WhenConfigured_ReturnsPlaintextConsumer()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:kafka"] = "localhost:9092" })
            .Build();
        KafkaConsumerFactory sut = new(configuration, KafkaOptions());

        using IConsumer<Ignore, string> consumer = sut.Create("main");

        consumer.Should().NotBeNull();
    }
}
