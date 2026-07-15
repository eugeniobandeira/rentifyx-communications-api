namespace RentifyxCommunications.Application.Abstractions;

public sealed record DynamoDbOptions(string NotificationsTableName = "notifications");
