variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "github_repo" {
  description = "GitHub repository in owner/repo format (e.g. eugeniobandeira/rentifyx-communications-api)"
  type        = string
}

variable "ecr_repository_arn" {
  description = "ARN of the ECR repository the workflow will push to"
  type        = string
}

variable "ec2_instance_arn" {
  description = "ARN of the EC2 instance the workflow will send SSM commands to"
  type        = string
}

variable "create_oidc_provider" {
  description = <<-EOT
    AWS allows only one IAM OIDC provider per account per issuer URL.
    rentifyx-platform's module.github_actions_oidc already created
    token.actions.githubusercontent.com for real in this account
    (166613156216) - default false here so this module looks up that
    existing provider instead of failing with EntityAlreadyExists.
  EOT
  type        = bool
  default     = false
}
