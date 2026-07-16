using System.Globalization;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

public static class ConsentItemMapper
{
    public static ConsentPreference ToEntity(Dictionary<string, AttributeValue> item, Guid recipientId, Channel channel)
    {
        bool optedIn = item["OptedIn"].BOOL ?? false;
        DateTime updatedAt = DateTime.Parse(item["UpdatedAt"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        return ConsentPreference.Create(recipientId, channel, optedIn, updatedAt).Value;
    }

    public static Dictionary<string, AttributeValue> ToItem(ConsentPreference consent) =>
        new()
        {
            [NotificationTableSchema.PartitionKey] = new($"CONSENT#{consent.RecipientId}"),
            [NotificationTableSchema.SortKey] = new($"CHANNEL#{consent.Channel}"),
            ["OptedIn"] = new AttributeValue { BOOL = consent.OptedIn },
            ["UpdatedAt"] = new(consent.UpdatedAt.ToString("O", CultureInfo.InvariantCulture))
        };
}
