locals {
  prefix = "${var.app_name}-communications-${var.environment}"

  common_tags = {
    Application = var.app_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

provider "aws" {
  region  = var.aws_region
  profile = "rentifyx-admin"

  default_tags {
    tags = local.common_tags
  }
}

# Cross-repo: rentifyx-platform's module.kafka.client_iam_policy_json (MSK
# Serverless access) and module.ses.identity_arn (shared SES sender
# identity), via terraform_remote_state rather than duplicating either by
# hand and risking drift. Read-only - this repo's own AWS credentials
# already have access to that state (same account/bucket this repo's own
# backend uses). The Kafka policy is wrapped in try(..., "") below since
# rentifyx-platform's network/kafka modules were applied later than this
# block was first written; module.ses is not optional the same way.
data "terraform_remote_state" "platform" {
  backend = "s3"

  config = {
    bucket = "rentifyx-tfstate-166613156216"
    key    = "platform/terraform.tfstate"
    region = "us-east-1"
  }
}

module "dynamodb" {
  source      = "./modules/dynamodb"
  prefix      = local.prefix
  environment = var.environment
}

module "ses" {
  source = "./modules/ses"
}

module "kms" {
  source      = "./modules/kms"
  prefix      = local.prefix
  environment = var.environment
}

module "secrets" {
  source           = "./modules/secrets"
  environment      = var.environment
  kms_key_arn      = module.kms.key_arn
  ses_identity_arn = data.terraform_remote_state.platform.outputs.ses_identity_arn
}

module "iam" {
  source             = "./modules/iam"
  prefix             = local.prefix
  table_arn          = module.dynamodb.table_arn
  kms_key_arn        = module.kms.key_arn
  ses_arn_secret_arn = module.secrets.ses_arn_secret_arn
  api_key_secret_arn = module.secrets.api_key_secret_arn
  ses_identity_arn   = data.terraform_remote_state.platform.outputs.ses_identity_arn
}

module "ec2" {
  count = var.enable_ec2 ? 1 : 0

  source                   = "./modules/ec2"
  prefix                   = local.prefix
  environment              = var.environment
  policy_arn               = module.iam.policy_arn
  aws_region               = var.aws_region
  dynamodb_table_name      = module.dynamodb.table_name
  ssh_key_name             = var.ssh_key_name
  kafka_client_policy_json = try(data.terraform_remote_state.platform.outputs.kafka_client_iam_policy_json, "")
  vpc_id                   = data.terraform_remote_state.platform.outputs.vpc_id
  subnet_id                = data.terraform_remote_state.platform.outputs.public_subnets[0]
}

data "aws_caller_identity" "main" {}

module "github_actions" {
  count = var.enable_ec2 && var.enable_github_actions ? 1 : 0

  source             = "./modules/github-actions"
  prefix             = local.prefix
  github_repo        = var.github_repo
  ecr_repository_arn = module.ec2[0].ecr_repository_arn
  ec2_instance_arn   = "arn:aws:ec2:${var.aws_region}:${data.aws_caller_identity.main.account_id}:instance/${module.ec2[0].instance_id}"
}
