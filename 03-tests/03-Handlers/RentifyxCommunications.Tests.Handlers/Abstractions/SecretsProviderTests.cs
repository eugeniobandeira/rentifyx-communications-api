using System.Reflection;
using FluentAssertions;
using RentifyxCommunications.Application.Abstractions;
using Xunit;

namespace RentifyxCommunications.Tests.Handlers.Abstractions;

public sealed class SecretsProviderTests
{
    [Fact]
    public void ISecretsProvider_IsDefinedInApplicationAssembly()
    {
        typeof(ISecretsProvider).Namespace.Should().Be("RentifyxCommunications.Application.Abstractions");
        typeof(ISecretsProvider).Assembly.Should().BeSameAs(typeof(SecretsProviderOptions).Assembly);
        typeof(ISecretsProvider).Assembly.GetName().Name.Should().Be("RentifyxCommunications.Application");
    }

    [Fact]
    public void ISecretsProvider_DeclaresGetSecretAsync()
    {
        MethodInfo? method = typeof(ISecretsProvider).GetMethod(nameof(ISecretsProvider.GetSecretAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<string>>();

        ParameterInfo[] parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be<string>();
        parameters[1].ParameterType.Should().Be<CancellationToken>();
        parameters[1].HasDefaultValue.Should().BeTrue();
    }

    [Fact]
    public void SecretsProviderOptions_HasExpectedProperties()
    {
        SecretsProviderOptions options = new("ses-arn", "kafka-user", "kafka-pass", "api-key");

        options.SesArn.Should().Be("ses-arn");
        options.KafkaSaslUsername.Should().Be("kafka-user");
        options.KafkaSaslPassword.Should().Be("kafka-pass");
        options.ApiKey.Should().Be("api-key");
    }
}
