using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Application.Abstractions;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Infrastructure.Repositories.Notifications;

public sealed class DynamoDbConsentAuditRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> dynamoDbOptions) : IConsentAuditRepository
{
    private readonly string _tableName = dynamoDbOptions.Value.NotificationsTableName;

    public Task AddAsync(ConsentAuditEntry entry, CancellationToken cancellationToken = default) =>
        client.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = ConsentAuditItemMapper.ToItem(entry)
        }, cancellationToken);
}
