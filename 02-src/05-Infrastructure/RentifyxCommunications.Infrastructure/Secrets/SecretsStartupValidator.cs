using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Abstractions;

namespace RentifyxCommunications.Infrastructure.Secrets;

public sealed class SecretsStartupValidator(
    ISecretsProvider secretsProvider,
    SecretsProviderOptions options,
    ILogger<SecretsStartupValidator> logger)
{
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        (string Name, string Key)[] requiredSecrets =
        [
            (nameof(SecretsProviderOptions.SesArn), options.SesArn),
            (nameof(SecretsProviderOptions.KafkaSaslUsername), options.KafkaSaslUsername),
            (nameof(SecretsProviderOptions.KafkaSaslPassword), options.KafkaSaslPassword),
        ];

        foreach ((string name, string key) in requiredSecrets)
        {
            try
            {
                await secretsProvider.GetSecretAsync(key, ct);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Required secret '{SecretName}' not found. Startup aborted.", name);
                throw new InvalidOperationException($"Required secret '{name}' not found. Startup aborted.", ex);
            }
        }
    }
}
