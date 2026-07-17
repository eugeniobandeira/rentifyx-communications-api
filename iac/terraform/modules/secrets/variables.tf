variable "environment" {
  type = string
}

variable "kms_key_arn" {
  description = "ARN of the KMS key used to encrypt secrets at rest"
  type        = string
}

variable "ses_identity_arn" {
  description = "ARN of the verified SES email identity"
  type        = string
}
