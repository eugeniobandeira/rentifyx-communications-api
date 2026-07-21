variable "prefix" {
  description = "Resource name prefix"
  type        = string
}

variable "environment" {
  description = "Deployment environment"
  type        = string
}

variable "policy_arn" {
  description = "ARN of the least-privilege IAM policy to attach to the instance profile"
  type        = string
}

variable "aws_region" {
  description = "AWS region"
  type        = string
}

variable "dynamodb_table_name" {
  description = "DynamoDB notifications table name"
  type        = string
}

variable "ssh_key_name" {
  description = "EC2 key pair name for SSH access (leave empty to disable SSH)"
  type        = string
  default     = ""
}

variable "vpc_id" {
  description = "VPC ID to provision the security group in - rentifyx-platform's VPC, read via terraform_remote_state, so this instance can reach the MSK Serverless cluster (VPC-internal only)."
  type        = string
}

variable "subnet_id" {
  description = "Subnet ID to provision the instance in - one of rentifyx-platform's public subnets, read via terraform_remote_state."
  type        = string
}

variable "kafka_client_policy_json" {
  description = <<-EOT
    IAM policy JSON granting MSK Serverless access, from
    rentifyx-platform's module.kafka.client_iam_policy_json output (read via
    terraform_remote_state in the root module). Empty string disables this
    attachment entirely - used until rentifyx-platform's network/kafka
    modules are actually applied (their outputs don't exist yet).
  EOT
  type        = string
  default     = ""
}
