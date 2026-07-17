output "ses_arn_secret_arn" {
  value = aws_secretsmanager_secret.ses_arn.arn
}

output "api_key_secret_arn" {
  value = aws_secretsmanager_secret.api_key.arn
}
