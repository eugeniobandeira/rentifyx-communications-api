using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Testcontainers.LocalStack;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Secrets;

public sealed class LocalStackSecretsManagerFixture : IAsyncLifetime
{
    private LocalStackContainer? _container;

    public IAmazonSecretsManager Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new LocalStackBuilder("localstack/localstack:3")
            .Build();

        await _container.StartAsync();

        Client = new AmazonSecretsManagerClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSecretsManagerConfig { ServiceURL = _container.GetConnectionString() });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        if (_container is not null)
            await _container.DisposeAsync();
    }

    public Task SeedSecretAsync(string key, string value, CancellationToken ct = default) =>
        Client.CreateSecretAsync(new CreateSecretRequest { Name = key, SecretString = value }, ct);
}

[CollectionDefinition(nameof(LocalStackFixtureGroup))]
public sealed class LocalStackFixtureGroup : ICollectionFixture<LocalStackSecretsManagerFixture>;
