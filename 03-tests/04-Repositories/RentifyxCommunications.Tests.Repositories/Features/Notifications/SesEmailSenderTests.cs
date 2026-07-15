using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using ErrorOr;
using FluentAssertions;
using Moq;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Email;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class SesEmailSenderTests
{
    [Fact]
    public async Task SendAsync_WhenSesThrows_ShouldReturnFailureError()
    {
        Mock<IAmazonSimpleEmailService> client = new();
        client
            .Setup(c => c.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleEmailServiceException("throttled"));

        Mock<ISecretsProvider> secretsProvider = new();
        secretsProvider
            .Setup(p => p.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("arn:aws:ses:us-east-1:000000000000:identity/sender@example.com");

        SecretsProviderOptions options = new("test-ses-arn", "test-kafka-user", "test-kafka-pass");
        SesEmailSender sut = new(client.Object, secretsProvider.Object, options);
        EmailAddress recipient = EmailAddress.Create("recipient@example.com").Value;

        ErrorOr<Success> result = await sut.SendAsync(recipient, "rendered content");

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Failure);
    }
}
