#!/bin/bash
set -euo pipefail

# Install Docker
dnf install -y docker
systemctl enable --now docker

# Log in to ECR and pull the image
aws ecr get-login-password --region ${aws_region} \
  | docker login --username AWS --password-stdin ${ecr_repository_url}

docker pull ${ecr_repository_url}:latest

# Run the API container (restarts automatically on failure or reboot)
docker run -d \
  --name rentifyx-communications-api \
  --restart unless-stopped \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e AWS__Region=${aws_region} \
  -e DynamoDb__NotificationsTableName=${dynamodb_table_name} \
%{ if kafka_bootstrap_servers != "" }
  -e ConnectionStrings__kafka=${kafka_bootstrap_servers} \
%{ endif }
  ${ecr_repository_url}:latest
