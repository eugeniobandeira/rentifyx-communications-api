using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

public sealed class DynamoDbNotificationRepository(IAmazonDynamoDB client) : INotificationRepository
{
    public const string TableName = "notifications";

    public async Task<bool> SaveIfNotExistsAsync(NotificationEntity notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = TableName,
                Item = NotificationItemMapper.ToItem(notification),
                ConditionExpression = "attribute_not_exists(PK)"
            }, cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<NotificationEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        QueryResponse response = await client.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            IndexName = "GSI2",
            KeyConditionExpression = "GSI2PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"ID#{id}")
            }
        }, cancellationToken);

        return response.Items.Count == 0 ? null : NotificationItemMapper.ToEntity(response.Items[0]);
    }

    public async Task<NotificationEntity?> GetByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        GetItemResponse response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"NOTIF#{correlationId}"),
                ["SK"] = new("METADATA")
            }
        }, cancellationToken);

        return response.IsItemSet ? NotificationItemMapper.ToEntity(response.Item) : null;
    }

    public async Task<IReadOnlyList<NotificationEntity>> GetByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        QueryResponse response = await client.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            IndexName = "GSI1",
            KeyConditionExpression = "GSI1PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"RECIPIENT#{recipientId}")
            }
        }, cancellationToken);

        return response.Items.Select(NotificationItemMapper.ToEntity).ToList();
    }

    public async Task UpdateStatusAsync(Guid id, NotificationStatus status, string? failureReason = null, CancellationToken cancellationToken = default)
    {
        NotificationEntity? notification = await GetByIdAsync(id, cancellationToken);
        if (notification is null)
            return;

        string updatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        string updateExpression = "SET #status = :status, UpdatedAt = :updatedAt, GSI3PK = :gsi3pk, GSI3SK = :updatedAt";
        Dictionary<string, AttributeValue> expressionAttributeValues = new()
        {
            [":status"] = new(status.ToString()),
            [":updatedAt"] = new(updatedAt),
            [":gsi3pk"] = new($"STATUS#{status}")
        };

        if (failureReason is not null)
        {
            updateExpression += ", FailureReason = :failureReason";
            expressionAttributeValues[":failureReason"] = new(failureReason);
        }

        await client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"NOTIF#{notification.CorrelationId}"),
                ["SK"] = new("METADATA")
            },
            UpdateExpression = updateExpression,
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = expressionAttributeValues
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationEntity>> GetStuckDispatchingAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        string threshold = (DateTime.UtcNow - olderThan).ToString("O", CultureInfo.InvariantCulture);

        QueryResponse response = await client.QueryAsync(new QueryRequest
        {
            TableName = TableName,
            IndexName = "GSI3",
            KeyConditionExpression = "GSI3PK = :pk AND GSI3SK < :threshold",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"STATUS#{NotificationStatus.Dispatching}"),
                [":threshold"] = new(threshold)
            }
        }, cancellationToken);

        return response.Items.Select(NotificationItemMapper.ToEntity).ToList();
    }
}
