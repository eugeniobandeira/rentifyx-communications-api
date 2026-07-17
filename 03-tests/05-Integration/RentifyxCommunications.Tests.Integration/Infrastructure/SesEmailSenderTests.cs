using ErrorOr;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Email;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
[Collection(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class SesEmailSenderTests(LocalStackNotificationInfrastructureFixture fixture)
{
    [Fact]
    public async Task SendAsync_AgainstLocalStack_ShouldSucceed()
    {
        await fixture.VerifySenderAsync("sender@example.com");

        Mock<ISecretsProvider> secretsProvider = new();
        secretsProvider
            .Setup(p => p.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("arn:aws:ses:us-east-1:000000000000:identity/sender@example.com");

        SecretsProviderOptions options = new("test-ses-arn", "test-api-key");
        SesEmailSender sut = new(fixture.Ses, secretsProvider.Object, options);
        EmailAddress recipient = EmailAddress.Create("recipient@example.com").Value;

        ErrorOr<Success> result = await sut.SendAsync(recipient, "rendered content");

        result.IsError.Should().BeFalse();
    }
}
