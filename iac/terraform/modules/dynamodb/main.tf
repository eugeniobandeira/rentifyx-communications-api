# Single-table design - schema matches
# RentifyxCommunications.Domain.Constants.NotificationTableSchema exactly
# (PK/SK, GSI1/GSI2/GSI3, TTL attribute name "TTL"). Keep both in sync.
resource "aws_dynamodb_table" "notifications" {
  name         = "${var.prefix}-notifications"
  billing_mode = "PAY_PER_REQUEST"
  table_class  = "STANDARD"

  hash_key  = "PK"
  range_key = "SK"

  attribute {
    name = "PK"
    type = "S"
  }

  attribute {
    name = "SK"
    type = "S"
  }

  attribute {
    name = "GSI1PK"
    type = "S"
  }

  attribute {
    name = "GSI1SK"
    type = "S"
  }

  attribute {
    name = "GSI2PK"
    type = "S"
  }

  attribute {
    name = "GSI2SK"
    type = "S"
  }

  attribute {
    name = "GSI3PK"
    type = "S"
  }

  attribute {
    name = "GSI3SK"
    type = "S"
  }

  # NotificationItemMapper: GSI1PK=RECIPIENT#{id}, GSI1SK=NOTIF#{createdAt}#{id}
  # (GetNotificationsByRecipient query, ordered by creation time).
  global_secondary_index {
    name            = "GSI1"
    hash_key        = "GSI1PK"
    range_key       = "GSI1SK"
    projection_type = "ALL"
  }

  # NotificationItemMapper: GSI2PK=GSI2SK=ID#{id} (GetById by internal id,
  # distinct from the PK's correlationId).
  global_secondary_index {
    name            = "GSI2"
    hash_key        = "GSI2PK"
    range_key       = "GSI2SK"
    projection_type = "ALL"
  }

  # NotificationItemMapper: GSI3PK=STATUS#{status}, GSI3SK=lastUpdated.
  # ReconciliationHostedService polls this for notifications stuck in
  # Dispatching past a staleness threshold.
  global_secondary_index {
    name            = "GSI3"
    hash_key        = "GSI3PK"
    range_key       = "GSI3SK"
    projection_type = "ALL"
  }

  # 90-day auto-expiry (LGPD Art. 46 data minimization) - see
  # NotificationItemMapper.TtlDays.
  ttl {
    attribute_name = "TTL"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}
