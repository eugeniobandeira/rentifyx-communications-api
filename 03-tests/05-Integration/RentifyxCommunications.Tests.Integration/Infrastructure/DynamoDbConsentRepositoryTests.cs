using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Repositories.Notifications;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
[Collection(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class DynamoDbConsentRepositoryTests(LocalStackNotificationInfrastructureFixture fixture)
{
    private readonly DynamoDbConsentRepository _sut = new(fixture.DynamoDb, Options.Create(new DynamoDbOptions()));

    [Fact]
    public async Task FindAsync_WithNoRecord_ShouldReturnNull()
    {
        ConsentPreference? result = await _sut.FindAsync(Guid.NewGuid(), Channel.Email);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_WithSeededOptedOutRecord_ShouldReturnHydratedPreference()
    {
        Guid recipientId = Guid.NewGuid();
        DateTime updatedAt = DateTime.UtcNow;

        await fixture.DynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = new DynamoDbOptions().NotificationsTableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"CONSENT#{recipientId}"),
                ["SK"] = new($"CHANNEL#{Channel.Email}"),
                ["OptedIn"] = new AttributeValue { BOOL = false },
                ["UpdatedAt"] = new(updatedAt.ToString("O"))
            }
        });

        ConsentPreference? result = await _sut.FindAsync(recipientId, Channel.Email);

        result.Should().NotBeNull();
        result!.RecipientId.Should().Be(recipientId);
        result.Channel.Should().Be(Channel.Email);
        result.OptedIn.Should().BeFalse();
    }
}
