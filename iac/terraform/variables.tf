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

variable "enable_ec2" {
  description = "Provision the EC2 deploy target (instance, ECR repo, security group). Disable for a lightweight dev bootstrap that only needs DynamoDB/SES/KMS/Secrets."
  type        = bool
  default     = true
}

variable "enable_github_actions" {
  description = "Provision the GitHub Actions OIDC deploy role. Requires enable_ec2 = true (it grants access to the EC2 instance and ECR repo); ignored otherwise."
  type        = bool
  default     = true
}
