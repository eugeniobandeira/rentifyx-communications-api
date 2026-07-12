namespace RentifyxCommunications.Application.Abstractions;

public interface ISecretsProvider
{
    Task<string> GetSecretAsync(string key, CancellationToken ct = default);
}
