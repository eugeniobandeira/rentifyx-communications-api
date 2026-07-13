using FluentAssertions;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Repositories;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
[Collection(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class DynamoDbNotificationRepositoryTests(LocalStackNotificationInfrastructureFixture fixture)
{
    private readonly DynamoDbNotificationRepository _sut = new(fixture.DynamoDb);

    private static NotificationEntity CreateNotification()
    {
        return NotificationEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("user@example.com").Value,
            Channel.Email,
            TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;
    }

    [Fact]
    public async Task SaveIfNotExistsAsync_WithNewNotification_ShouldReturnTrue()
    {
        NotificationEntity notification = CreateNotification();

        bool saved = await _sut.SaveIfNotExistsAsync(notification);

        saved.Should().BeTrue();
    }

    [Fact]
    public async Task SaveIfNotExistsAsync_CalledTwiceWithSameCorrelationId_ShouldReturnFalseOnSecondCall()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);

        NotificationEntity duplicate = NotificationEntity.Create(
            notification.CorrelationId,
            Guid.NewGuid(),
            EmailAddress.Create("other@example.com").Value,
            Channel.Email,
            TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Bob" }).Value;

        bool savedAgain = await _sut.SaveIfNotExistsAsync(duplicate);

        savedAgain.Should().BeFalse();
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingNotification_ShouldReturnHydratedEntity()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);

        NotificationEntity? result = await _sut.GetByIdAsync(notification.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(notification.Id);
        result.CorrelationId.Should().Be(notification.CorrelationId);
        result.RecipientId.Should().Be(notification.RecipientId);
        result.Recipient.Value.Should().Be("user@example.com");
        result.Channel.Should().Be(Channel.Email);
        result.Status.Should().Be(NotificationStatus.Pending);
    }

    [Fact]
    public async Task GetByIdAsync_WithMissingNotification_ShouldReturnNull()
    {
        NotificationEntity? result = await _sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
