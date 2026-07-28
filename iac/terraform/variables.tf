variable "aws_region" {
  description = "AWS region to deploy resources"
  type        = string
  default     = "sa-east-1"
}

variable "environment" {
  description = "Deployment environment (production, staging, development)"
  type        = string
  default     = "production"
}

variable "app_name" {
  description = "Application name used as resource name prefix"
  type        = string
  default     = "rentifyx"
}

variable "ssh_key_name" {
  description = "EC2 key pair name for SSH access (leave empty to disable SSH)"
  type        = string
  default     = ""
}

variable "github_repo" {
  description = "GitHub repository in owner/repo format allowed to assume the deploy role"
  type        = string
  default     = "eugeniobandeira/rentifyx-communications-api"
}

variable "frontend_base_url" {
  description = <<-EOT
    Base URL of the deployed rentityx-frontend, used to build links in
    outbound emails (verify-email, etc). rentityx-frontend has no fixed
    domain yet, only an EC2 public IP - update this (and re-apply) whenever
    that instance is replaced. Empty string falls back to the container's
    own hardcoded default (see modules/ec2/variables.tf).
  EOT
  type        = string
  default     = "http://54.20.34.102:4000"
}

variable "enable_ec2" {
  description = "Provision the EC2 deploy target (instance, ECR repo, security group). Disable for a lightweight dev bootstrap that only needs DynamoDB/SES/KMS/Secrets."
  type        = bool
  default     = true
}

variable "enable_github_actions" {
  description = <<-EOT
    Provision the GitHub Actions OIDC deploy role. Requires enable_ec2 = true
    (it grants access to the EC2 instance and ECR repo); ignored otherwise.
    Defaults to false: this module's data lookup for the shared
    token.actions.githubusercontent.com OIDC provider (created by
    rentifyx-platform's module.github_actions_oidc) fails outright if that
    provider doesn't currently exist in the account, breaking every plan/
    destroy by default. Set to true only once that provider is confirmed
    live and a real CI/CD pipeline is ready to use this role.
  EOT
  type        = bool
  default     = false
}
