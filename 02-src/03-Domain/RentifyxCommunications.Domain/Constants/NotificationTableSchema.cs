namespace RentifyxCommunications.Domain.Constants;

/// <summary>
/// DynamoDB single-table schema names shared by every reader/writer of the notifications
/// table (repository, item mapper, and test fixtures that provision the table) so a rename
/// in one place can't silently drift from the others.
/// </summary>
public static class NotificationTableSchema
{
    public const string PartitionKey = "PK";
    public const string SortKey = "SK";

    public const string Gsi1IndexName = "GSI1";
    public const string Gsi1PartitionKey = "GSI1PK";
    public const string Gsi1SortKey = "GSI1SK";

    public const string Gsi2IndexName = "GSI2";
    public const string Gsi2PartitionKey = "GSI2PK";
    public const string Gsi2SortKey = "GSI2SK";

    public const string Gsi3IndexName = "GSI3";
    public const string Gsi3PartitionKey = "GSI3PK";
    public const string Gsi3SortKey = "GSI3SK";

    public const string ConsentPartitionKeyPrefix = "CONSENT#";
    public const string ConsentAuditSortKeyPrefix = "AUDIT#";
}
