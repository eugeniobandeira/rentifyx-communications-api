using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories;

public sealed class DynamoDbNotificationRepository(IAmazonDynamoDB client) : INotificationRepository
{
    public const string TableName = "notifications";
    private const int TtlDays = 90;

    public async Task<bool> SaveIfNotExistsAsync(NotificationEntity notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = TableName,
                Item = ToItem(notification),
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

        return response.Items.Count == 0 ? null : FromItem(response.Items[0]);
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

        return response.Items.Select(FromItem).ToList();
    }

    public async Task UpdateStatusAsync(Guid id, NotificationStatus status, CancellationToken cancellationToken = default)
    {
        NotificationEntity? notification = await GetByIdAsync(id, cancellationToken);
        if (notification is null)
            return;

        DateTime updatedAt = DateTime.UtcNow;

        await client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"NOTIF#{notification.CorrelationId}"),
                ["SK"] = new("METADATA")
            },
            UpdateExpression = "SET #status = :status, UpdatedAt = :updatedAt",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new(status.ToString()),
                [":updatedAt"] = new(updatedAt.ToString("O", CultureInfo.InvariantCulture))
            }
        }, cancellationToken);
    }

    private static Dictionary<string, AttributeValue> ToItem(NotificationEntity notification)
    {
        long ttlEpochSeconds = new DateTimeOffset(notification.CreatedAt.AddDays(TtlDays)).ToUnixTimeSeconds();

        Dictionary<string, AttributeValue> item = new()
        {
            ["PK"] = new($"NOTIF#{notification.CorrelationId}"),
            ["SK"] = new("METADATA"),
            ["GSI1PK"] = new($"RECIPIENT#{notification.RecipientId}"),
            ["GSI1SK"] = new($"NOTIF#{notification.CreatedAt:O}#{notification.Id}"),
            ["GSI2PK"] = new($"ID#{notification.Id}"),
            ["GSI2SK"] = new($"ID#{notification.Id}"),
            ["Id"] = new(notification.Id.ToString()),
            ["CorrelationId"] = new(notification.CorrelationId.ToString()),
            ["RecipientId"] = new(notification.RecipientId.ToString()),
            ["RecipientEmail"] = new(notification.Recipient.Value),
            ["Channel"] = new(notification.Channel.ToString()),
            ["TemplateId"] = new(notification.TemplateId.Value),
            ["Payload"] = new(JsonSerializer.Serialize(notification.Payload)),
            ["Status"] = new(notification.Status.ToString()),
            ["CreatedAt"] = new(notification.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            ["TTL"] = new AttributeValue { N = ttlEpochSeconds.ToString(CultureInfo.InvariantCulture) }
        };

        if (notification.FailureReason is not null)
            item["FailureReason"] = new AttributeValue(notification.FailureReason);

        if (notification.UpdatedAt is not null)
            item["UpdatedAt"] = new AttributeValue(notification.UpdatedAt.Value.ToString("O", CultureInfo.InvariantCulture));

        return item;
    }

    private static NotificationEntity FromItem(Dictionary<string, AttributeValue> item)
    {
        EmailAddress recipient = EmailAddress.Create(item["RecipientEmail"].S).Value;
        TemplateId templateId = TemplateId.Create(item["TemplateId"].S).Value;
        IReadOnlyDictionary<string, string> payload = JsonSerializer.Deserialize<Dictionary<string, string>>(item["Payload"].S)!;

        return NotificationEntity.Rehydrate(
            id: Guid.Parse(item["Id"].S),
            correlationId: Guid.Parse(item["CorrelationId"].S),
            recipientId: Guid.Parse(item["RecipientId"].S),
            recipient: recipient,
            channel: Enum.Parse<Channel>(item["Channel"].S),
            templateId: templateId,
            payload: payload,
            status: Enum.Parse<NotificationStatus>(item["Status"].S),
            failureReason: item.TryGetValue("FailureReason", out AttributeValue? reason) ? reason.S : null,
            createdAt: DateTime.Parse(item["CreatedAt"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            updatedAt: item.TryGetValue("UpdatedAt", out AttributeValue? updatedAt)
                ? DateTime.Parse(updatedAt.S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : null);
    }
}
