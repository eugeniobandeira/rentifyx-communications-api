using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Caching.Memory;
using RentifyxCommunications.Application.Abstractions;

namespace RentifyxCommunications.Infrastructure.Secrets;

public sealed class SecretsManagerProvider(IAmazonSecretsManager client, IMemoryCache cache) : ISecretsProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string> GetSecretAsync(string key, CancellationToken ct = default)
    {
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
            return cached;

        GetSecretValueResponse response = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = key },
            ct);

        string value = response.SecretString
            ?? throw new InvalidOperationException($"Secret '{key}' has no string value.");

        cache.Set(key, value, CacheDuration);

        return value;
    }
}
