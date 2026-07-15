using System.Globalization;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Repositories.Notifications;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
[Collection(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class DynamoDbNotificationRepositoryTests(LocalStackNotificationInfrastructureFixture fixture)
{
    private readonly DynamoDbNotificationRepository _sut = new(fixture.DynamoDb, Options.Create(new DynamoDbOptions()));

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

    [Fact]
    public async Task GetByRecipientAsync_WithMultipleNotifications_ShouldReturnAllForThatRecipient()
    {
        Guid recipientId = Guid.NewGuid();
        NotificationEntity first = NotificationEntity.Create(
            Guid.NewGuid(), recipientId, EmailAddress.Create("user@example.com").Value,
            Channel.Email, TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;
        NotificationEntity second = NotificationEntity.Create(
            Guid.NewGuid(), recipientId, EmailAddress.Create("user@example.com").Value,
            Channel.Email, TemplateId.Create("welcome-email").Value,
            new Dictionary<string, string> { ["name"] = "Alice" }).Value;
        NotificationEntity otherRecipient = CreateNotification();

        await _sut.SaveIfNotExistsAsync(first);
        await _sut.SaveIfNotExistsAsync(second);
        await _sut.SaveIfNotExistsAsync(otherRecipient);

        IReadOnlyList<NotificationEntity> results = await _sut.GetByRecipientAsync(recipientId);

        results.Should().HaveCount(2);
        results.Select(n => n.Id).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldUpdateOnlyStatusAndUpdatedAt()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);

        await _sut.UpdateStatusAsync(notification.Id, NotificationStatus.Sent);

        NotificationEntity? result = await _sut.GetByIdAsync(notification.Id);
        result.Should().NotBeNull();
        result!.Status.Should().Be(NotificationStatus.Sent);
        result.UpdatedAt.Should().NotBeNull();
        result.Recipient.Value.Should().Be("user@example.com");
        result.Payload.Should().ContainKey("name").WhoseValue.Should().Be("Alice");
    }

    [Fact]
    public async Task UpdateStatusAsync_ShouldPersistFailureReason_WhenProvided()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);

        await _sut.UpdateStatusAsync(notification.Id, NotificationStatus.Failed, "SES rejected the message");

        NotificationEntity? result = await _sut.GetByIdAsync(notification.Id);
        result.Should().NotBeNull();
        result!.Status.Should().Be(NotificationStatus.Failed);
        result.FailureReason.Should().Be("SES rejected the message");
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_WithExistingNotification_ShouldReturnHydratedEntity()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);

        NotificationEntity? result = await _sut.GetByCorrelationIdAsync(notification.CorrelationId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(notification.Id);
        result.CorrelationId.Should().Be(notification.CorrelationId);
    }

    [Fact]
    public async Task GetByCorrelationIdAsync_WithMissingNotification_ShouldReturnNull()
    {
        NotificationEntity? result = await _sut.GetByCorrelationIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStuckDispatchingAsync_WithOldDispatchingRecord_ShouldReturnIt()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);
        await _sut.UpdateStatusAsync(notification.Id, NotificationStatus.Dispatching);
        await BackdateGsi3Async(notification.CorrelationId, TimeSpan.FromMinutes(5));

        IReadOnlyList<NotificationEntity> stuck = await _sut.GetStuckDispatchingAsync(TimeSpan.FromMinutes(2));

        stuck.Select(n => n.Id).Should().Contain(notification.Id);
    }

    [Fact]
    public async Task GetStuckDispatchingAsync_WithRecentDispatchingRecord_ShouldExcludeIt()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);
        await _sut.UpdateStatusAsync(notification.Id, NotificationStatus.Dispatching);

        IReadOnlyList<NotificationEntity> stuck = await _sut.GetStuckDispatchingAsync(TimeSpan.FromMinutes(2));

        stuck.Select(n => n.Id).Should().NotContain(notification.Id);
    }

    [Fact]
    public async Task GetStuckDispatchingAsync_WithOldSentRecord_ShouldExcludeIt()
    {
        NotificationEntity notification = CreateNotification();
        await _sut.SaveIfNotExistsAsync(notification);
        await _sut.UpdateStatusAsync(notification.Id, NotificationStatus.Sent);
        await BackdateGsi3Async(notification.CorrelationId, TimeSpan.FromMinutes(5));

        IReadOnlyList<NotificationEntity> stuck = await _sut.GetStuckDispatchingAsync(TimeSpan.FromMinutes(2));

        stuck.Select(n => n.Id).Should().NotContain(notification.Id);
    }

    private async Task BackdateGsi3Async(Guid correlationId, TimeSpan age)
    {
        string backdated = (DateTime.UtcNow - age).ToString("O", CultureInfo.InvariantCulture);

        await fixture.DynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = new DynamoDbOptions().NotificationsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"NOTIF#{correlationId}"),
                ["SK"] = new("METADATA")
            },
            UpdateExpression = "SET GSI3SK = :backdated, UpdatedAt = :backdated",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":backdated"] = new(backdated)
            }
        });
    }
}
