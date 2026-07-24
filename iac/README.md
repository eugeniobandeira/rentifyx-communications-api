# Infrastructure as Code

Terraform definitions for the AWS resources this service needs. This is the **real, current**
infra for the RentifyX Communications API — not a template placeholder.

```
iac/
├── README.md          ← you are here
└── terraform/
    ├── main.tf         # provider, cross-repo remote state, module wiring
    ├── variables.tf    # root input variables (all have defaults)
    ├── outputs.tf      # table/KMS/IAM/EC2/ECR/GitHub-deploy-role outputs
    ├── backend.tf       # empty S3 backend skeleton (values via -backend-config flags)
    └── modules/
        ├── dynamodb/       # notifications single-table
        ├── kms/            # customer-managed encryption key
        ├── secrets/        # Secrets Manager entries
        ├── ses/            # per-app SESv2 configuration set
        ├── iam/            # least-privilege app policy
        ├── ec2/             # deploy target: instance + ECR + security group
        └── github-actions/  # OIDC deploy role for CI
```

`k8s/` (repo root) contains Kustomize manifests, but **it is not the deploy path used today** —
see [Kubernetes vs. the real deploy path](#kubernetes-vs-the-real-deploy-path) below. The actual
deploy target is the EC2 instance provisioned by `modules/ec2`.

## Modules

| Module | Provisions |
|---|---|
| `dynamodb` | The single-table `{prefix}-notifications` (`PAY_PER_REQUEST`, `STANDARD` class). PK/SK plus GSI1 (`RECIPIENT#{id}` — recipient history), GSI2 (`ID#{id}` — lookup by internal id, since PK is keyed by `correlationId` for idempotency), GSI3 (`STATUS#{status}`/`UpdatedAt` — polled by `ReconciliationHostedService`). 90-day TTL on the `TTL` attribute (LGPD Art. 46 data minimization), point-in-time recovery, and encryption at rest all enabled. Schema must stay in sync with `RentifyxCommunications.Domain.Constants.NotificationTableSchema`. |
| `kms` | A single customer-managed KMS key (`enable_key_rotation = true`, 30-day deletion window) plus an alias (`alias/{prefix}-secrets`). Used to encrypt both the Secrets Manager entries and DynamoDB data. |
| `secrets` | Two Secrets Manager secrets: `rentifyx/comms/ses-arn` (populated directly from the real SES identity ARN — not sensitive, no manual step needed) and `rentifyx/comms/api-key` (seeded with a placeholder `"REPLACE_AT_DEPLOY_TIME"`, `lifecycle.ignore_changes` on `secret_string` so Terraform never clobbers the real value once it's set manually or by CI). Both encrypted with `module.kms`'s key. `SecretsManagerProvider.GetSecretAsync(key)` treats each as its own secret **name**, not a JSON blob — keep `appsettings.json`'s `SecretsProvider` section in sync with these names. |
| `ses` | An SESv2 **configuration set** (`rentifyx-communications`) with bounce/complaint suppression and reputation-metrics tracking. It does **not** create the SES sender identity itself — that's owned once, centrally, by `rentifyx-platform`'s `module.ses` and shared cross-repo (see [Cross-repo dependency](#cross-repo-dependency-rentifyx-platform) below), because SES identities are unique per AWS account and both this repo and `rentifyx-identity-api` previously collided trying to each own a copy. |
| `iam` | One least-privilege IAM policy (`{prefix}-api-policy`) granting: DynamoDB `GetItem`/`PutItem`/`UpdateItem`/`Query` on the notifications table and its indexes; KMS `Decrypt`/`Encrypt`/`GenerateDataKey` on the key above; `secretsmanager:GetSecretValue` on the two secrets above; `ses:SendEmail`/`SendRawEmail` on the shared SES identity. This is the policy attached to the EC2 instance profile. |
| `ec2` | The actual deploy target — gated behind `var.enable_ec2` (default `true`). Creates: an ECR repository (`{prefix}-communications-api`, keeps the last 5 images), an IAM role/instance profile (attaches `iam`'s policy plus `AmazonSSMManagedInstanceCore` plus an inline ECR-pull policy), a security group (inbound 8080 always, inbound 22 only if `ssh_key_name` is set, all egress open), and a `t2.micro` Amazon Linux 2023 instance (30 GiB gp3 root volume, encrypted) whose `user_data` (`userdata.sh.tpl`) bootstraps the container with the ECR image URL, DynamoDB table name, and Kafka bootstrap servers. AMI updates are pinned via `lifecycle.ignore_changes = [ami]` so a new al2023 patch release doesn't force a surprise replace on an unrelated `plan`. |
| `github-actions` | A GitHub Actions OIDC deploy role, gated behind `var.enable_ec2 && var.enable_github_actions` (both default `true`). Grants the CI workflow `ecr:*Push*`/`GetAuthorizationToken` on this repo's ECR repo and `ssm:SendCommand`/`GetCommandInvocation` to deploy onto the EC2 instance via SSM `RunShellScript` — no long-lived AWS credentials as GitHub secrets. The AssumeRoleWithWebIdentity condition restricts this to `repo:eugeniobandeira/rentifyx-communications-api:ref:refs/heads/main`. |

## Initializing

The backend is deliberately empty in `backend.tf` (an `s3 {}` skeleton) — real values are supplied
via `-backend-config` flags at `init` time, matching `rentifyx-identity-api`'s convention:

```bash
cd iac/terraform/
terraform init \
  -backend-config="bucket=rentifyx-tfstate-166613156216" \
  -backend-config="key=communications-api/terraform.tfstate" \
  -backend-config="region=us-east-1" \
  -backend-config="dynamodb_table=rentifyx-tflock"
terraform plan
```

This is the same command already documented in the repo root [`README.md`](../README.md)'s
"Infrastructure" section — keep both in sync if the backend values ever change.

## Variables and `terraform.tfvars`

Every variable in the root `variables.tf` has a default — there is **no variable that will hard-fail
`plan`/`apply` for lack of a value**. In practice you still want a `terraform.tfvars` (or
`-var` flags) for anything environment-specific, since the defaults are tuned for a single
production-like account:

| Variable | Default | When to override |
|---|---|---|
| `aws_region` | `sa-east-1` | Rarely — the cross-repo remote state read (`data.terraform_remote_state.platform`) is hardcoded to `us-east-1`/`rentifyx-tfstate-166613156216` regardless of this value, since that's where `rentifyx-platform`'s own state lives. |
| `environment` | `production` | Set to `staging`/`development` for a non-prod stack — feeds the `{prefix}` used by every resource name and tag. |
| `app_name` | `rentifyx` | Only if the whole platform is renamed. |
| `ssh_key_name` | `""` (SSH disabled) | Set to an existing EC2 key pair name to open port 22 on the instance's security group. |
| `github_repo` | `eugeniobandeira/rentifyx-communications-api` | Only if the repo is forked/renamed. |
| `enable_ec2` | `true` | See [`enable_ec2` / `enable_github_actions`](#enable_ec2--enable_github_actions) below. |
| `enable_github_actions` | `true` | See below. |

There is also no `.tfvars.example` checked in today — the table above is the closest thing. A
minimal `terraform.tfvars` for a non-default environment looks like:

```hcl
environment = "staging"
ssh_key_name = "rentifyx-deploy-temp"
```

The AWS provider itself uses a fixed named profile, `rentifyx-admin` (`main.tf`'s `provider "aws"`
block) — not `var.aws_profile` (no such variable exists). Make sure that profile is configured
locally (`aws configure --profile rentifyx-admin`) before running `plan`/`apply`.

## Cross-repo dependency: `rentifyx-platform`

This repo does **not** provision its own networking or shared SES identity. `main.tf` reads them
straight out of `rentifyx-platform`'s state via `data.terraform_remote_state.platform` (same S3
bucket this repo's own backend uses, key `platform/terraform.tfstate`, region `us-east-1`):

| Output consumed | Used for |
|---|---|
| `vpc_id` | `module.ec2`'s security group — so the instance can reach the self-hosted Kafka broker (VPC-internal). |
| `public_subnets[0]` | The subnet `module.ec2`'s instance is launched into. |
| `kafka_ssm_parameter_path` | Looked up via `data.aws_ssm_parameter.kafka_bootstrap_servers` (only when `enable_ec2 = true`) to get the self-hosted Kafka broker's bootstrap address, passed into the container as `ConnectionStrings__kafka`. Wrapped in `try(..., "")` since `rentifyx-platform`'s Kafka module may not have been applied yet — **without a real value here the container crash-loops at boot** (`KafkaConsumerFactory` throws). |
| `ses_identity_arn` | The shared SES sender identity ARN — consumed by `module.secrets` (stored as the `rentifyx/comms/ses-arn` secret) and `module.iam` (scopes the `ses:SendEmail` policy statement). |

**Apply order matters:** `rentifyx-platform` must be applied first (it owns the VPC/subnets, the
shared SES identity, the Kafka broker, and — in the common case — the GitHub OIDC provider).
`rentifyx-identity-api` applies next. `rentifyx-communications-api` (this repo) applies last, since
its `module.github_actions` looks up the OIDC provider by URL rather than creating it
(`create_oidc_provider` defaults to `false` here) — AWS allows only one
`token.actions.githubusercontent.com` OIDC provider per account, and whichever of the three repos
applies first "owns" it. Tearing down runs in the reverse order: destroy this repo and
`rentifyx-identity-api` **before** `rentifyx-platform`, since both read its outputs via remote
state and would otherwise plan against a state that no longer exists.

## `enable_ec2` / `enable_github_actions`

- **`enable_ec2`** (default `true`) gates `module.ec2` entirely, plus the Kafka SSM parameter
  lookup that feeds it. Set to `false` for a lightweight bootstrap that only needs
  DynamoDB/SES/KMS/Secrets/IAM — no EC2 instance, no ECR repo, no security group, and no attempt
  to read the Kafka SSM parameter at all.
- **`enable_github_actions`** (default `true`) gates `module.github_actions` — the CI/CD OIDC deploy
  role. It's `count = var.enable_ec2 && var.enable_github_actions ? 1 : 0`, so it's silently
  skipped whenever `enable_ec2 = false` even if this flag is `true` (there'd be nothing to deploy
  to or push images for).

## Applying and tearing down

```bash
terraform plan
terraform apply
```

Real, billable resources this creates: the EC2 instance and the ECR repository. Core infra
(DynamoDB/KMS/Secrets/IAM/EC2/GitHub OIDC role) has been applied for real against AWS and verified
end-to-end (Kafka → this service → SES, confirmed with a real delivered email) — see
`.specs/project/STATE.md` for the session history. It is torn down between working sessions to
avoid ongoing cost, so **no infrastructure from this module is currently live** unless someone has
just run `apply`.

```bash
terraform destroy
```

Destroy this repo and `rentifyx-identity-api` before destroying `rentifyx-platform` (see
[Cross-repo dependency](#cross-repo-dependency-rentifyx-platform) above).

## Kubernetes vs. the real deploy path

`k8s/` (repo root) has a working Kustomize base + `dev`/`prod` overlays (Deployment, Service, HPA
min 2/max 6, liveness/readiness probes, PodDisruptionBudget). It is **not** wired to any CI
workflow and is not how this service is actually deployed today. The real, exercised deploy path
is: GitHub Actions builds the image, pushes it to the ECR repo created by `modules/ec2`, then
assumes `module.github_actions`'s deploy role to run an SSM `RunShellScript` command against the
EC2 instance created by the same module. Treat `k8s/` as available-but-unused infrastructure, not
a second supported deployment target, unless/until it's actually hooked up to a workflow.

## Authentication note

The API this infra supports authenticates inbound HTTP requests with a static **API key**, not
JWT — unlike `rentifyx-identity-api`. The key lives in the `rentifyx/comms/api-key` Secrets
Manager secret created by `modules/secrets` (seeded with a placeholder Terraform never overwrites
after the first apply — see the `secrets` module row above) and is validated per-request via
`ApiKeyAuthenticationHandler` against the `X-Api-Key` header. There is no end-user identity flow
on this API — it's service-to-service only.
