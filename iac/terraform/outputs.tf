output "table_name" {
  value = module.dynamodb.table_name
}

output "table_arn" {
  value = module.dynamodb.table_arn
}

output "kms_key_arn" {
  value = module.kms.key_arn
}

output "ses_identity_arn" {
  value = module.ses.identity_arn
}

output "iam_policy_arn" {
  value = module.iam.policy_arn
}

output "ec2_public_ip" {
  value = one(module.ec2[*].public_ip)
}

output "ec2_public_dns" {
  value = one(module.ec2[*].public_dns)
}

output "ecr_repository_url" {
  value = one(module.ec2[*].ecr_repository_url)
}

output "ec2_role_arn" {
  value = one(module.ec2[*].ec2_role_arn)
}

output "github_deploy_role_arn" {
  description = "Set as GH Actions secret AWS_DEPLOY_ROLE_ARN"
  value       = one(module.github_actions[*].deploy_role_arn)
}
