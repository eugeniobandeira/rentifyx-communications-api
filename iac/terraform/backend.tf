terraform {
  required_version = ">= 1.7"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Empty on purpose: values supplied via -backend-config flags at `terraform
  # init` time (bucket=rentifyx-tfstate-166613156216,
  # key=communications-api/terraform.tfstate, region=us-east-1,
  # dynamodb_table=rentifyx-tflock), not hardcoded here - matches
  # rentifyx-identity-api's convention. Terraform requires at least this
  # empty `backend "s3" {}` skeleton for CLI-flag partial configuration to
  # persist correctly between commands.
  backend "s3" {}
}
