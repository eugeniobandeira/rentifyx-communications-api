# E-01 · Project Foundation & DevSecOps Pipeline — Specification

## Problem Statement

Before any domain or application code can be written, the project needs a working local environment, a tested solution scaffold, and a CI/CD pipeline with security gates. Without this foundation, every subsequent epic risks discovering environment or config problems mid-implementation. The goal of E-01 is to make Day 2 frictionless: `dotnet run --project AppHost` boots everything, secrets load cleanly, the Kafka consumer starts gracefully, and CI catches security regressions automatically.

## Goals

- [ ] `dotnet run --project AppHost` boots all services (API, Kafka) with zero manual steps, connecting to a real AWS dev/sandbox account for DynamoDB/SES/SecretsManager/KMS (AD-012, 2026-07-11 — supersedes the original LocalStack plan)
- [ ] Kafka consumer starts and stops gracefully with the host lifecycle (no messages lost on shutdown)
- [ ] Secrets (SES ARN, Kafka credentials) load from Secrets Manager — never from appsettings or env vars committed to git
- [ ] CI pipeline passes: build → test → coverage ≥80% → OWASP dependency check → Trivy image scan
- [ ] git-secrets pre-commit hook blocks any accidental secret commit

## Out of Scope

| Feature | Reason |
| --- | --- |
| Domain model, aggregates, value objects | E-02 |
| SES actual email sending | E-04 |
| DynamoDB table implementation | E-04 |
| Kafka message consumption logic | E-03 |
| HTTP endpoints beyond health check | E-05 |
| Terraform / Helm / IaC | E-06 |
| Actual Kafka message processing | Consumer is registered and lifecycle-wired only; it doesn't process any messages yet |

---

## User Stories

### P1: US-C001 — Solution Scaffold ⭐ MVP

**User Story**: As a dev, I want the clean solution scaffold so I can start coding domain and application layers without friction.

**Why P1**: Nothing else can be built without the solution structure in place. This is day zero.

**Acceptance Criteria**:

1. WHEN `dotnet new clean-arch -n RentifyX.Communications` is run THEN the solution SHALL contain projects: `RentifyX.Communications.API`, `RentifyX.Communications.Application`, `RentifyX.Communications.Domain`, `RentifyX.Communications.Infrastructure`, `RentifyX.Communications.Tests` (unit + integration)
2. WHEN the solution builds THEN it SHALL compile with zero warnings (treat warnings as errors via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)
3. WHEN the API project starts THEN it SHALL expose a `/health` endpoint returning 200 OK
4. WHEN the API project starts THEN Serilog SHALL be configured as the logging provider with structured JSON output
5. WHEN an unhandled exception occurs THEN `GlobalExceptionHandler` SHALL return a `ProblemDetails` response (RFC 7807) and SHALL NOT leak stack traces in production
6. WHEN the API starts in Development THEN Scalar UI SHALL be accessible at `/scalar`
7. WHEN any request arrives THEN `CorrelationId` middleware SHALL attach/propagate a correlation ID in request and response headers
8. WHEN `.editorconfig` is present THEN CA5xxx security analyzer rules SHALL be enforced at build time

**Independent Test**: `dotnet build` produces zero warnings; `GET /health` returns 200; `GET /scalar` loads Scalar UI; POST to a non-existent route returns structured ProblemDetails.

---

### P1: US-C002 — Local Dev Environment (Aspire + real AWS dev account + Kafka) ⭐ MVP

**User Story**: As a dev, I want a Kafka container via Aspire and the AWS SDK configured against a real dev/sandbox account (DynamoDB, SES, SecretsManager, KMS) so I can develop and test against real AWS behavior, not an emulator.

**Why P1**: Without a working local environment, integration tests and local development can't run at all. (Superseded 2026-07-11 — AD-012: LocalStack was rejected as adding no value; the team develops against a real AWS dev/sandbox account instead.)

**Acceptance Criteria**:

1. WHEN `dotnet run --project AppHost` is run THEN it SHALL start the API host and a Kafka container; the API SHALL connect to DynamoDB/SES/SecretsManager/KMS in the configured AWS dev/sandbox account (via a named credentials profile), not an emulator
2. WHEN the AWS dev/sandbox account is provisioned (manually, outside this task) THEN it SHALL have: DynamoDB tables `notifications` and `delivery-log`, and a verified SES sender identity
3. WHEN Kafka starts THEN it SHALL be reachable at the configured broker address from within the API host
4. WHEN the AppHost starts THEN Aspire dashboard SHALL display health status for all resources
5. WHEN Kafka fails to start THEN the AppHost SHALL surface a clear error (not silently proceed)
6. WHEN AWS credentials are missing or invalid THEN the application SHALL fail fast at startup with a clear error, not hang or silently degrade

**Independent Test**: Run `dotnet run --project AppHost`; verify Aspire dashboard shows all resources healthy; confirm the API can read/write to the real dev-account DynamoDB tables and send via SES; confirm Kafka broker is reachable.

---

### P1: US-C003 — Kafka Consumer Hosted Service ⭐ MVP

**User Story**: As a dev, I want `NotificationRequestedConsumer` registered as an `IHostedService` so the Kafka consumer lifecycle is tied to the host and drains cleanly on shutdown.

**Why P1**: The consumer must be registered before E-03 implements consumption logic. Getting lifecycle wiring right before message processing prevents subtle shutdown bugs later.

**Acceptance Criteria**:

1. WHEN the API host starts THEN `NotificationRequestedConsumer` SHALL start consuming the `notification-requested` topic (no messages to process yet — just connected and polling)
2. WHEN `SIGTERM` or host shutdown is triggered THEN `StopAsync` SHALL drain any in-flight message (complete current processing) before the consumer exits — no abrupt kill
3. WHEN the consumer starts THEN it SHALL log its consumer group ID and the topic it is subscribed to at `Information` level
4. WHEN the consumer cannot connect to Kafka on startup THEN it SHALL log the error at `Error` level and SHALL NOT crash the entire host — it SHALL retry with backoff
5. WHEN the host is running THEN the Aspire dashboard SHALL show `NotificationRequestedConsumer` as a registered hosted service

**Independent Test**: Start AppHost; observe startup logs showing consumer connected; send `SIGTERM`; confirm graceful shutdown log entry for the consumer before process exit.

---

### P1: US-C004 — CI/CD Pipeline with Security Gates ⭐ MVP

**User Story**: As a tech lead, I want the same security gates as identity-api so quality and security standards stay consistent across the platform.

**Why P1**: CI must be in place before any feature branch is merged. Security gates that are added later tend never to catch the things they were added to catch.

**Acceptance Criteria**:

1. WHEN a PR is opened THEN GitHub Actions SHALL run: `dotnet build` → `dotnet test` → coverage report → OWASP dependency-check → Trivy image scan, in that order
2. WHEN test coverage is below 80% THEN the CI pipeline SHALL fail and block the merge
3. WHEN a dependency with a known HIGH or CRITICAL CVE is introduced THEN OWASP dependency-check SHALL fail the pipeline
4. WHEN the Docker image is built THEN Trivy SHALL scan it and fail the pipeline on HIGH or CRITICAL vulnerabilities
5. WHEN branch protection rules are set THEN a PR SHALL require: CI pipeline green + at least 1 approved review before merge to `main`
6. WHEN the CI pipeline passes THEN it SHALL produce: a test results artifact, a coverage report artifact, and a Trivy scan report artifact

**Independent Test**: Open a PR with a known-bad dependency; confirm CI fails on OWASP step. Open a PR with <80% coverage; confirm CI fails on coverage step.

---

### P1: US-C005 — Secrets Management (no committed secrets) ⭐ MVP

**User Story**: As a dev, I want secrets loaded from AWS Secrets Manager at startup so SES credentials and Kafka SASL credentials are never in source control.

**Why P1**: A committed secret is an immediate security incident. This must be true from commit one.

**Acceptance Criteria**:

1. WHEN the application starts THEN it SHALL load the following from Secrets Manager: SES sender identity ARN, Kafka SASL username, Kafka SASL password
2. WHEN `ISecretsProvider` is injected THEN it SHALL abstract the Secrets Manager client behind a typed interface (not `IConfiguration` directly)
3. WHEN a secret is fetched THEN it SHALL be cached for 5 minutes to avoid excess Secrets Manager API calls
4. WHEN a required secret is missing or inaccessible THEN the application SHALL log a descriptive error at `Critical` level and fail fast on startup (not start in a broken state)
5. WHEN running locally via Aspire THEN the same `ISecretsProvider` SHALL work against the real AWS Secrets Manager in the dev/sandbox account (no separate code path for local vs. prod — AD-012)
6. WHEN `git commit` is run THEN the git-secrets pre-commit hook SHALL scan staged files and block the commit if any secret pattern (AWS key format, connection string pattern) is detected
7. WHEN `appsettings.json` or any committed config file is inspected THEN it SHALL contain zero secret values — only non-sensitive defaults and placeholder references

**Independent Test**: Remove a required secret from LocalStack and confirm startup fails with a clear error. Add a fake AWS key pattern to a staged file and confirm git-secrets blocks the commit.

---

## Edge Cases

- WHEN AWS credentials for the dev/sandbox account are invalid or the account is unreachable THEN the API SHALL fail fast with a clear error, not hang indefinitely
- WHEN Kafka broker is unreachable on consumer startup THEN consumer SHALL retry with exponential backoff (not crash the host)
- WHEN dev-account DynamoDB tables or the SES sender identity don't exist yet THEN startup SHALL fail with a clear "resource not found" error rather than a cryptic AWS SDK exception (provisioning is currently manual, not automated by this service)
- WHEN `dotnet build` is run with CA5xxx rules enabled THEN any use of insecure crypto APIs SHALL be a build error, not a warning
- WHEN the OWASP check runs and a transitive (indirect) dependency has a HIGH CVE THEN the pipeline SHALL still fail (not only direct dependencies)
- WHEN secrets cache TTL expires mid-request THEN the provider SHALL refresh transparently without returning stale or null values

---

## Requirement Traceability

| Requirement ID | Story | Status |
| --- | --- | --- |
| E01-01 | US-C001: Solution layers present and compiling | Done |
| E01-02 | US-C001: Zero build warnings (TreatWarningsAsErrors) | Done |
| E01-03 | US-C001: /health returns 200 | Done |
| E01-04 | US-C001: Serilog structured JSON logging | Done |
| E01-05 | US-C001: GlobalExceptionHandler returns ProblemDetails, no stack traces | Done |
| E01-06 | US-C001: Scalar UI at /scalar in Development | Done |
| E01-07 | US-C001: CorrelationId middleware on all requests | Done |
| E01-08 | US-C001: CA5xxx rules enforced at build | Done |
| E01-09 | US-C002: AppHost boots API + Kafka; API connects to real AWS dev account | Pending — reworked per AD-012 |
| E01-10 | US-C002: Dev-account DynamoDB tables + SES identity exist (manual provisioning) | Pending — reworked per AD-012 |
| E01-11 | US-C002: *(retired — was LocalStack init idempotency; no init script exists post-AD-012)* | N/A |
| E01-12 | US-C002: Kafka reachable from API host | Pending |
| E01-13 | US-C003: Consumer starts and subscribes to notification-requested topic | Pending |
| E01-14 | US-C003: StopAsync drains in-flight before exit | Pending |
| E01-15 | US-C003: Consumer logs group ID + topic at startup | Pending |
| E01-16 | US-C003: Kafka connection failure does not crash host | Pending |
| E01-17 | US-C004: CI runs build → test → coverage → OWASP → Trivy | Pending |
| E01-18 | US-C004: Coverage <80% fails CI | Pending |
| E01-19 | US-C004: HIGH/CRITICAL CVE fails OWASP step | Pending |
| E01-20 | US-C004: HIGH/CRITICAL image vuln fails Trivy step | Pending |
| E01-21 | US-C004: Branch protection requires CI green + 1 review | Pending |
| E01-22 | US-C005: Secrets Manager loads SES ARN + Kafka credentials | Pending |
| E01-23 | US-C005: ISecretsProvider typed abstraction | Pending |
| E01-24 | US-C005: 5-minute secret cache | Pending |
| E01-25 | US-C005: Missing secret = Critical log + fail fast | Pending |
| E01-26 | US-C005: ISecretsProvider works against real AWS Secrets Manager, same code path local/prod | Pending |
| E01-27 | US-C005: git-secrets hook blocks secret patterns in staged files | Pending |
| E01-28 | US-C005: Zero secrets in committed config files | Pending |

**Coverage:** 28 requirements defined; E01-11 retired 2026-07-11 (AD-012 — no LocalStack init script exists to be idempotent) — 27 active, 8 done (E01-01–E01-08), 19 pending — see `tasks.md` for full traceability

---

## Success Criteria

- [ ] `dotnet run --project AppHost` boots all containers with zero manual steps and Aspire dashboard shows all resources healthy
- [ ] `NotificationRequestedConsumer` starts, subscribes, and shuts down gracefully (visible in logs)
- [ ] `dotnet build` produces zero warnings
- [ ] CI pipeline runs end-to-end and fails correctly on: coverage <80%, HIGH CVE dependency, HIGH vuln in image
- [ ] git-secrets hook blocks a test commit containing a fake AWS key pattern
- [ ] No secret values present in any committed file (`git grep` for key patterns returns empty)
