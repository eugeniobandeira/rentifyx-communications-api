output "deploy_role_arn" {
  description = "ARN of the IAM role assumed by the GitHub Actions deploy workflow"
  value       = aws_iam_role.github_deploy.arn
}
