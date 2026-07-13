using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using RentifyxCommunications.Infrastructure.Repositories;
using Testcontainers.LocalStack;
using Xunit;

namespace RentifyxCommunications.Tests.Integration.Infrastructure;

public sealed class LocalStackNotificationInfrastructureFixture : IAsyncLifetime
{
    private LocalStackContainer? _container;

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;
    public IAmazonSimpleEmailServiceV2 Ses { get; private set; } = null!;

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

        Ses = new AmazonSimpleEmailServiceV2Client(
            credentials,
            new AmazonSimpleEmailServiceV2Config { ServiceURL = serviceUrl });

        await CreateNotificationsTableAsync();
    }

    public async Task DisposeAsync()
    {
        DynamoDb.Dispose();
        Ses.Dispose();

        if (_container is not null)
            await _container.DisposeAsync();
    }

    private async Task CreateNotificationsTableAsync()
    {
        await DynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = DynamoDbNotificationRepository.TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema =
            [
                new KeySchemaElement("PK", KeyType.HASH),
                new KeySchemaElement("SK", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("PK", ScalarAttributeType.S),
                new AttributeDefinition("SK", ScalarAttributeType.S),
                new AttributeDefinition("GSI1PK", ScalarAttributeType.S),
                new AttributeDefinition("GSI1SK", ScalarAttributeType.S),
                new AttributeDefinition("GSI2PK", ScalarAttributeType.S),
                new AttributeDefinition("GSI2SK", ScalarAttributeType.S)
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = "GSI1",
                    KeySchema =
                    [
                        new KeySchemaElement("GSI1PK", KeyType.HASH),
                        new KeySchemaElement("GSI1SK", KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                },
                new GlobalSecondaryIndex
                {
                    IndexName = "GSI2",
                    KeySchema =
                    [
                        new KeySchemaElement("GSI2PK", KeyType.HASH),
                        new KeySchemaElement("GSI2SK", KeyType.RANGE)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ]
        });
    }
}

[CollectionDefinition(nameof(NotificationInfrastructureFixtureGroup))]
public sealed class NotificationInfrastructureFixtureGroup : ICollectionFixture<LocalStackNotificationInfrastructureFixture>;
