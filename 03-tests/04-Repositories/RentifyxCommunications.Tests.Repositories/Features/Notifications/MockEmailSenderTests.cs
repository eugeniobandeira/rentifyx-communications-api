using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Email;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class MockEmailSenderTests
{
    [Fact]
    public async Task SendAsync_ShouldRecordCallAndReturnSuccess()
    {
        MockEmailSender sut = new();
        EmailAddress recipient = EmailAddress.Create("user@example.com").Value;

        ErrorOr<Success> result = await sut.SendAsync(recipient, "rendered content");

        result.IsError.Should().BeFalse();
        sut.SentEmails.Should().ContainSingle(e => e.Recipient == recipient && e.RenderedContent == "rendered content");
    }
}
