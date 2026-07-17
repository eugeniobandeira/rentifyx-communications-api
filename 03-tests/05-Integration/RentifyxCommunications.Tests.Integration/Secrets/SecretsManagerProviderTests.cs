using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Infrastructure.Secrets;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Secrets;

[Trait("Category", "Integration")]
[Collection(nameof(LocalStackFixtureGroup))]
public sealed class SecretsManagerProviderTests(LocalStackSecretsManagerFixture fixture)
{
    [Fact]
    public async Task GetSecretAsync_ReturnsValue_WhenSecretExistsInLocalStack()
    {
        string key = $"test/{Guid.NewGuid()}/secret";
        await fixture.SeedSecretAsync(key, "super-secret-value");

        using MemoryCache cache = new(new MemoryCacheOptions());
        SecretsManagerProvider provider = new(fixture.Client, cache);

        string value = await provider.GetSecretAsync(key);

        value.Should().Be("super-secret-value");
    }

    [Fact]
    public async Task ValidateAsync_Throws_AndLogsCritical_WhenARequiredSecretIsMissing()
    {
        string prefix = Guid.NewGuid().ToString();
        string sesArnKey = $"test/{prefix}/ses-arn";
        string apiKeyKey = $"test/{prefix}/api-key";

        await fixture.SeedSecretAsync(sesArnKey, "arn:aws:ses:us-east-1:000000000000:identity/example.com");
        // apiKeyKey is intentionally never seeded

        using MemoryCache cache = new(new MemoryCacheOptions());
        SecretsManagerProvider provider = new(fixture.Client, cache);
        SecretsProviderOptions options = new(sesArnKey, apiKeyKey);
        ListLogger logger = new();
        SecretsStartupValidator validator = new(provider, Options.Create(options), logger);

        Func<Task> act = () => validator.ValidateAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical &&
            e.Message.Contains(nameof(SecretsProviderOptions.ApiKey), StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetSecretAsync_UsesCache_OnSecondCallWithinWindow()
    {
        Mock<IAmazonSecretsManager> client = new();
        client
            .Setup(c => c.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = "cached-value" });

        using MemoryCache cache = new(new MemoryCacheOptions());
        SecretsManagerProvider provider = new(client.Object, cache);

        string first = await provider.GetSecretAsync("some-key");
        string second = await provider.GetSecretAsync("some-key");

        first.Should().Be("cached-value");
        second.Should().Be("cached-value");
        client.Verify(
            c => c.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class ListLogger : ILogger<SecretsStartupValidator>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
