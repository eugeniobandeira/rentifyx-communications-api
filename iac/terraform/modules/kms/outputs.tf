output "key_arn" {
  value = aws_kms_key.secrets.arn
}

output "key_id" {
  value = aws_kms_key.secrets.key_id
}
