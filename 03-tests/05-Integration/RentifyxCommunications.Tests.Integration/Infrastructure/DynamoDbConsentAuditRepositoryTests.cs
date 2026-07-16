using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Repositories.Notifications;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
[Collection(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class DynamoDbConsentAuditRepositoryTests(LocalStackNotificationInfrastructureFixture fixture)
{
    private readonly DynamoDbConsentAuditRepository _sut = new(fixture.DynamoDb, Options.Create(new DynamoDbOptions(LocalStackNotificationInfrastructureFixture.TableName)));

    [Fact]
    public async Task AddAsync_WritesItem_QueryablByRecipientAndAuditPrefix()
    {
        Guid recipientId = Guid.NewGuid();
        ConsentAuditEntry entry = new(
            RecipientId: recipientId,
            Channel: Channel.Email,
            PreviousOptedIn: false,
            NewOptedIn: true,
            ChangedAt: DateTime.UtcNow);

        await _sut.AddAsync(entry);

        QueryResponse response = await fixture.DynamoDb.QueryAsync(new QueryRequest
        {
            TableName = LocalStackNotificationInfrastructureFixture.TableName,
            KeyConditionExpression = $"{NotificationTableSchema.PartitionKey} = :pk AND begins_with({NotificationTableSchema.SortKey}, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"{NotificationTableSchema.ConsentPartitionKeyPrefix}{recipientId}"),
                [":skPrefix"] = new(NotificationTableSchema.ConsentAuditSortKeyPrefix)
            }
        });

        response.Items.Should().HaveCount(1);
        response.Items[0]["RecipientId"].S.Should().Be(recipientId.ToString());
    }

    [Fact]
    public async Task AddAsync_StoresChannelAsStringName_NotNumericValue()
    {
        Guid recipientId = Guid.NewGuid();
        ConsentAuditEntry entry = new(
            RecipientId: recipientId,
            Channel: Channel.Email,
            PreviousOptedIn: true,
            NewOptedIn: false,
            ChangedAt: DateTime.UtcNow);

        await _sut.AddAsync(entry);

        QueryResponse response = await fixture.DynamoDb.QueryAsync(new QueryRequest
        {
            TableName = LocalStackNotificationInfrastructureFixture.TableName,
            KeyConditionExpression = $"{NotificationTableSchema.PartitionKey} = :pk AND begins_with({NotificationTableSchema.SortKey}, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"{NotificationTableSchema.ConsentPartitionKeyPrefix}{recipientId}"),
                [":skPrefix"] = new(NotificationTableSchema.ConsentAuditSortKeyPrefix)
            }
        });

        AttributeValue channelAttribute = response.Items[0]["Channel"];
        channelAttribute.S.Should().Be(nameof(Channel.Email));
        channelAttribute.N.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AddAsync_WithNullPreviousOptedIn_RoundTripsAsAbsentAttribute()
    {
        Guid recipientId = Guid.NewGuid();
        ConsentAuditEntry entry = new(
            RecipientId: recipientId,
            Channel: Channel.Sms,
            PreviousOptedIn: null,
            NewOptedIn: true,
            ChangedAt: DateTime.UtcNow);

        await _sut.AddAsync(entry);

        QueryResponse response = await fixture.DynamoDb.QueryAsync(new QueryRequest
        {
            TableName = LocalStackNotificationInfrastructureFixture.TableName,
            KeyConditionExpression = $"{NotificationTableSchema.PartitionKey} = :pk AND begins_with({NotificationTableSchema.SortKey}, :skPrefix)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"{NotificationTableSchema.ConsentPartitionKeyPrefix}{recipientId}"),
                [":skPrefix"] = new(NotificationTableSchema.ConsentAuditSortKeyPrefix)
            }
        });

        response.Items.Should().HaveCount(1);
        response.Items[0].Should().NotContainKey("PreviousOptedIn");
    }
}
