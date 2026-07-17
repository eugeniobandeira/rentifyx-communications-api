output "instance_id" {
  value = aws_instance.communications_api.id
}

output "ecr_repository_arn" {
  value = aws_ecr_repository.communications_api.arn
}

output "public_ip" {
  value = aws_instance.communications_api.public_ip
}

output "public_dns" {
  value = aws_instance.communications_api.public_dns
}

output "ecr_repository_url" {
  value = aws_ecr_repository.communications_api.repository_url
}

output "ec2_role_arn" {
  value = aws_iam_role.ec2.arn
}
