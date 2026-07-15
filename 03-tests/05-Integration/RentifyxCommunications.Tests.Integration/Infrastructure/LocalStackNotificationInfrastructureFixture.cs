using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using RentifyxCommunications.Domain.Constants;
using Testcontainers.LocalStack;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

public sealed class LocalStackNotificationInfrastructureFixture : IAsyncLifetime
{
    public const string TableName = "notifications";

    private LocalStackContainer? _container;

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;
    public IAmazonSimpleEmailService Ses { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new LocalStackBuilder("localstack/localstack:3")
            .Build();

        await _container.StartAsync();

        BasicAWSCredentials credentials = new("test", "test");
        string serviceUrl = _container.GetConnectionString();

        DynamoDb = new AmazonDynamoDBClient(
            credentials,
            new AmazonDynamoDBConfig { ServiceURL = serviceUrl });

        Ses = new AmazonSimpleEmailServiceClient(
            credentials,
            new AmazonSimpleEmailServiceConfig { ServiceURL = serviceUrl });

        await CreateNotificationsTableAsync();
    }

    public async Task DisposeAsync()
    {
        DynamoDb.Dispose();
        Ses.Dispose();

        if (_container is not null)
            await _container.DisposeAsync();
    }

    public Task VerifySenderAsync(string emailAddress, CancellationToken cancellationToken = default) =>
        Ses.VerifyEmailAddressAsync(new VerifyEmailAddressRequest { EmailAddress = emailAddress }, cancellationToken);

    private async Task CreateNotificationsTableAsync()
    {
        await DynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema =
            [
                new KeySchemaElement(NotificationTableSchema.PartitionKey, KeyType.HASH),
                new KeySchemaElement(NotificationTableSchema.SortKey, KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition(NotificationTableSchema.PartitionKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.SortKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.Gsi1PartitionKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.Gsi1SortKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.Gsi2PartitionKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.Gsi2SortKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.Gsi3PartitionKey, ScalarAttributeType.S),
                new AttributeDefinition(NotificationTableSchema.Gsi3SortKey, ScalarAttributeType.S)
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = NotificationTableSchema.Gsi1IndexName,
                    KeySchema =
                    [
                        new KeySchemaElement(NotificationTableSchema.Gsi1PartitionKey, KeyType.HASH),
                        new KeySchemaElement(NotificationTableSchema.Gsi1SortKey, KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                },
                new GlobalSecondaryIndex
                {
                    IndexName = NotificationTableSchema.Gsi2IndexName,
                    KeySchema =
                    [
                        new KeySchemaElement(NotificationTableSchema.Gsi2PartitionKey, KeyType.HASH),
                        new KeySchemaElement(NotificationTableSchema.Gsi2SortKey, KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                },
                new GlobalSecondaryIndex
                {
                    IndexName = NotificationTableSchema.Gsi3IndexName,
                    KeySchema =
                    [
                        new KeySchemaElement(NotificationTableSchema.Gsi3PartitionKey, KeyType.HASH),
                        new KeySchemaElement(NotificationTableSchema.Gsi3SortKey, KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ]
        });
    }
}

[CollectionDefinition(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class NotificationInfrastructureFixtureGroup : ICollectionFixture<LocalStackNotificationInfrastructureFixture>;
