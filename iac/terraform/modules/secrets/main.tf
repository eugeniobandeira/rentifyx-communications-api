# Individual secrets, not one combined JSON blob - SecretsManagerProvider.GetSecretAsync(key)
# treats each SecretsProviderOptions path as its own Secrets Manager secret NAME (the whole
# secret's string value is the plain value, not a JSON key within it). Matches
# appsettings.json's "SecretsProvider" section exactly - keep both in sync.

resource "aws_secretsmanager_secret" "ses_arn" {
  name        = "rentifyx/comms/ses-arn"
  description = "SES email identity ARN used by SesEmailSender"
  kms_key_id  = var.kms_key_arn

  recovery_window_in_days = 0

  tags = {
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

# Not a placeholder - the SES identity ARN isn't sensitive, so it's populated
# directly from the real resource rather than requiring a manual deploy-time step.
resource "aws_secretsmanager_secret_version" "ses_arn" {
  secret_id     = aws_secretsmanager_secret.ses_arn.id
  secret_string = var.ses_identity_arn
}

resource "aws_secretsmanager_secret" "api_key" {
  name        = "rentifyx/comms/api-key"
  description = "Shared API key validated by ApiKeyAuthenticationHandler on incoming requests"
  kms_key_id  = var.kms_key_arn

  recovery_window_in_days = 0

  tags = {
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

resource "aws_secretsmanager_secret_version" "api_key" {
  secret_id     = aws_secretsmanager_secret.api_key.id
  secret_string = "REPLACE_AT_DEPLOY_TIME"

  lifecycle {
    ignore_changes = [secret_string]
  }
}
