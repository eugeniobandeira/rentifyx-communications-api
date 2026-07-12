namespace RentifyxCommunications.Application.Abstractions;

public sealed record SecretsProviderOptions(
    string SesArn = "rentifyx/comms/ses-arn",
    string KafkaSaslUsername = "rentifyx/comms/kafka-sasl-username",
    string KafkaSaslPassword = "rentifyx/comms/kafka-sasl-password");
