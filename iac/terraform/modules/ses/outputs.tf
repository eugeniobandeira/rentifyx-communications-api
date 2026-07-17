output "identity_arn" {
  value = aws_sesv2_email_identity.sender.arn
}

output "configuration_set_name" {
  value = aws_sesv2_configuration_set.communications.configuration_set_name
}
