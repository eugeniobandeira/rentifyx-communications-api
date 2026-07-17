using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

/// <summary>
/// Static mapper between <see cref="NotificationEntity"/> and a DynamoDB item,
/// centralizing enum-as-string persistence for <see cref="Channel"/>/<see cref="NotificationStatus"/>.
/// </summary>
public static class NotificationItemMapper
{
    private const int TtlDays = 90;

    public static Dictionary<string, AttributeValue> ToItem(NotificationEntity notification)
    {
        long ttlEpochSeconds = new DateTimeOffset(notification.CreatedAt.AddDays(TtlDays)).ToUnixTimeSeconds();

        string lastUpdated = (notification.UpdatedAt ?? notification.CreatedAt).ToString("O", CultureInfo.InvariantCulture);

        Dictionary<string, AttributeValue> item = new()
        {
            [NotificationTableSchema.PartitionKey] = new($"{NotificationTableSchema.NotificationPartitionKeyPrefix}{notification.CorrelationId}"),
            [NotificationTableSchema.SortKey] = new(NotificationTableSchema.MetadataSortKeyValue),
            [NotificationTableSchema.Gsi1PartitionKey] = new($"{NotificationTableSchema.RecipientPartitionKeyPrefix}{notification.RecipientId}"),
            [NotificationTableSchema.Gsi1SortKey] = new($"{NotificationTableSchema.NotificationPartitionKeyPrefix}{notification.CreatedAt:O}#{notification.Id}"),
            [NotificationTableSchema.Gsi2PartitionKey] = new($"{NotificationTableSchema.IdPartitionKeyPrefix}{notification.Id}"),
            [NotificationTableSchema.Gsi2SortKey] = new($"{NotificationTableSchema.IdPartitionKeyPrefix}{notification.Id}"),
            [NotificationTableSchema.Gsi3PartitionKey] = new($"{NotificationTableSchema.StatusPartitionKeyPrefix}{notification.Status}"),
            [NotificationTableSchema.Gsi3SortKey] = new(lastUpdated),
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

    public static NotificationEntity ToEntity(Dictionary<string, AttributeValue> item)
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
