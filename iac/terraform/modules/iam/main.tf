locals {
  # arn:aws:ses:<region>:<account_id>:identity/<name> - split rather than add a
  # data source, since region/account are already implicit in ses_identity_arn.
  ses_arn_parts = split(":", var.ses_identity_arn)
  ses_region    = local.ses_arn_parts[3]
  ses_account   = local.ses_arn_parts[4]
}

data "aws_iam_policy_document" "communications_api" {
  statement {
    sid    = "DynamoDBAccess"
    effect = "Allow"

    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem",
      "dynamodb:Query",
    ]

    resources = [
      var.table_arn,
      "${var.table_arn}/index/*",
    ]
  }

  statement {
    sid    = "KMSAccess"
    effect = "Allow"

    actions = [
      "kms:Decrypt",
      "kms:Encrypt",
      "kms:GenerateDataKey",
    ]

    resources = [var.kms_key_arn]
  }

  statement {
    sid    = "SecretsManagerAccess"
    effect = "Allow"

    actions = ["secretsmanager:GetSecretValue"]

    resources = [
      var.ses_arn_secret_arn,
      var.api_key_secret_arn,
    ]
  }

  statement {
    sid    = "SesSend"
    effect = "Allow"

    actions = ["ses:SendEmail", "ses:SendRawEmail"]

    # Confirmed 2026-07-25 via a real SES call failure: when the SES account is
    # in sandbox mode and the recipient is ALSO a verified identity in this
    # account (routine for sandbox testing), SES's IAM authorization checks
    # ses:SendEmail against the recipient's identity ARN too, not just the
    # sender's - scoping resources to only ses_identity_arn (the sender)
    # authorizes sends to unverified/external recipients fine, but fails with
    # AccessDenied the moment the recipient happens to be a verified identity
    # in this same account. Scoped to this account's own identity/* namespace
    # (not "*"), matching the pattern SES itself requires.
    resources = ["arn:aws:ses:${local.ses_region}:${local.ses_account}:identity/*"]
  }
}

resource "aws_iam_policy" "communications_api" {
  name        = "${var.prefix}-api-policy"
  description = "Least-privilege policy for the RentifyX Communications API"
  policy      = data.aws_iam_policy_document.communications_api.json
}
