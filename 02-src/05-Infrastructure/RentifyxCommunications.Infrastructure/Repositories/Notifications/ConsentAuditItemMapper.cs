using System.Globalization;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

public static class ConsentAuditItemMapper
{
    public static Dictionary<string, AttributeValue> ToItem(ConsentAuditEntry entry)
    {
        Dictionary<string, AttributeValue> item = new()
        {
            [NotificationTableSchema.PartitionKey] = new($"{NotificationTableSchema.ConsentPartitionKeyPrefix}{entry.RecipientId}"),
            [NotificationTableSchema.SortKey] = new(
                $"{NotificationTableSchema.ConsentAuditSortKeyPrefix}{entry.Channel}#{entry.ChangedAt:O}"),
            ["RecipientId"] = new(entry.RecipientId.ToString()),
            ["Channel"] = new(entry.Channel.ToString()),
            ["NewOptedIn"] = new AttributeValue { BOOL = entry.NewOptedIn },
            ["ChangedAt"] = new(entry.ChangedAt.ToString("O", CultureInfo.InvariantCulture))
        };

        if (entry.PreviousOptedIn is not null)
            item["PreviousOptedIn"] = new AttributeValue { BOOL = entry.PreviousOptedIn.Value };

        return item;
    }
}
