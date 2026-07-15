namespace RentifyxCommunications.Application.Abstractions;

public sealed record SecretsProviderOptions(
    string SesArn,
    string KafkaSaslUsername,
    string KafkaSaslPassword);
