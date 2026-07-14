using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories;

public sealed class DynamoDbConsentRepository(IAmazonDynamoDB client) : IConsentRepository
{
    public async Task<ConsentPreference?> FindAsync(Guid recipientId, Channel channel, CancellationToken cancellationToken = default)
    {
        GetItemResponse response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = DynamoDbNotificationRepository.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"CONSENT#{recipientId}"),
                ["SK"] = new($"CHANNEL#{channel}")
            }
        }, cancellationToken);

        if (!response.IsItemSet)
            return null;

        bool optedIn = response.Item["OptedIn"].BOOL ?? false;
        DateTime updatedAt = DateTime.Parse(response.Item["UpdatedAt"].S, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        return ConsentPreference.Create(recipientId, channel, optedIn, updatedAt).Value;
    }
}
