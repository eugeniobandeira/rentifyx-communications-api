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

    resources = [var.ses_identity_arn]
  }
}

resource "aws_iam_policy" "communications_api" {
  name        = "${var.prefix}-api-policy"
  description = "Least-privilege policy for the RentifyX Communications API"
  policy      = data.aws_iam_policy_document.communications_api.json
}
