#!/bin/bash
set -euo pipefail

# Install Docker
dnf install -y docker
systemctl enable --now docker

# Explicit SSM Agent install - this AL2023 AMI resolution does not ship the
# agent pre-installed despite AWS's docs describing AL2023 as including it
# by default (confirmed 2026-07-25 in rentifyx-platform's Kafka broker and
# rentifyx-identity-api's own EC2 instance). Do not assume it's present.
dnf install -y amazon-ssm-agent
systemctl enable --now amazon-ssm-agent

# Log in to ECR and pull the image
aws ecr get-login-password --region ${aws_region} \
  | docker login --username AWS --password-stdin ${ecr_repository_url}

docker pull ${ecr_repository_url}:latest

# Optional Kafka env, built as a plain shell variable rather than inlined
# inside the docker run backslash-continuation below - a Terraform template
# conditional directive without the whitespace-trim marker leaves a blank
# line in the rendered output, which breaks a backslash-continued multi-line
# command; with the trim marker it strips too much and glues unrelated
# tokens onto one line via a literal escaped space (not a token separator).
# A separate variable, expanded unquoted (so it word-splits into zero or
# two arguments), sidesteps both failure modes.
KAFKA_ENV=""
%{ if kafka_bootstrap_servers != "" }
KAFKA_ENV="-e ConnectionStrings__kafka=${kafka_bootstrap_servers}"
%{ endif }

# Run the API container (restarts automatically on failure or reboot)
docker run -d \
  --name rentifyx-communications-api \
  --restart unless-stopped \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e AWS__Region=${aws_region} \
  -e DynamoDb__NotificationsTableName=${dynamodb_table_name} \
  $KAFKA_ENV \
  ${ecr_repository_url}:latest
