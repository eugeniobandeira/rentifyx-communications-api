using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

public sealed class DynamoDbNotificationRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> dynamoDbOptions) : INotificationRepository
{
    private readonly string _tableName = dynamoDbOptions.Value.NotificationsTableName;

    public async Task<bool> SaveIfNotExistsAsync(NotificationEntity notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = _tableName,
                Item = NotificationItemMapper.ToItem(notification),
                ConditionExpression = $"attribute_not_exists({NotificationTableSchema.PartitionKey})"
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
            TableName = _tableName,
            IndexName = NotificationTableSchema.Gsi2IndexName,
            KeyConditionExpression = $"{NotificationTableSchema.Gsi2PartitionKey} = :pk",
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
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [NotificationTableSchema.PartitionKey] = new($"NOTIF#{correlationId}"),
                [NotificationTableSchema.SortKey] = new("METADATA")
            }
        }, cancellationToken);

        return response.IsItemSet ? NotificationItemMapper.ToEntity(response.Item) : null;
    }

    public async Task<IReadOnlyList<NotificationEntity>> GetByRecipientAsync(Guid recipientId, CancellationToken cancellationToken = default)
    {
        QueryResponse response = await client.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            IndexName = NotificationTableSchema.Gsi1IndexName,
            KeyConditionExpression = $"{NotificationTableSchema.Gsi1PartitionKey} = :pk",
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

        string updateExpression =
            $"SET #status = :status, UpdatedAt = :updatedAt, {NotificationTableSchema.Gsi3PartitionKey} = :gsi3pk, {NotificationTableSchema.Gsi3SortKey} = :updatedAt";
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
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [NotificationTableSchema.PartitionKey] = new($"NOTIF#{notification.CorrelationId}"),
                [NotificationTableSchema.SortKey] = new("METADATA")
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
            TableName = _tableName,
            IndexName = NotificationTableSchema.Gsi3IndexName,
            KeyConditionExpression = $"{NotificationTableSchema.Gsi3PartitionKey} = :pk AND {NotificationTableSchema.Gsi3SortKey} < :threshold",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"STATUS#{NotificationStatus.Dispatching}"),
                [":threshold"] = new(threshold)
            }
        }, cancellationToken);

        return response.Items.Select(NotificationItemMapper.ToEntity).ToList();
    }
}
