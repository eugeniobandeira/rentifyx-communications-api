using System.Globalization;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

/// <summary>
/// Static mapper between <see cref="ConsentPreference"/> and a DynamoDB item.
/// </summary>
public static class ConsentItemMapper
{
    public static ConsentPreference ToEntity(Dictionary<string, AttributeValue> item, Guid recipientId, Channel channel)
    {
        bool optedIn = item["OptedIn"].BOOL ?? false;
        DateTime updatedAt = DateTime.Parse(item["UpdatedAt"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        return ConsentPreference.Create(recipientId, channel, optedIn, updatedAt).Value;
    }
}
