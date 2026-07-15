using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

public sealed class DynamoDbConsentRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> dynamoDbOptions) : IConsentRepository
{
    private readonly string _tableName = dynamoDbOptions.Value.NotificationsTableName;

    public async Task<ConsentPreference?> FindAsync(Guid recipientId, Channel channel, CancellationToken cancellationToken = default)
    {
        GetItemResponse response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [NotificationTableSchema.PartitionKey] = new($"CONSENT#{recipientId}"),
                [NotificationTableSchema.SortKey] = new($"CHANNEL#{channel}")
            }
        }, cancellationToken);

        if (!response.IsItemSet)
            return null;

        return ConsentItemMapper.ToEntity(response.Item, recipientId, channel);
    }
}
