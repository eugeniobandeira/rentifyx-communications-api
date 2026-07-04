# RentifyX Communications API

**Vision:** A channel-agnostic notification microservice for the RentifyX platform that centralizes email delivery (v1) via AWS SES with Kafka-driven event intake, LGPD-compliant consent enforcement, and production-grade reliability.
**For:** Internal microservices (identity-api, asset-registry-api, leasing-api, billing-api, risk-api) that need to notify platform users without managing delivery, retries, or compliance themselves.
**Solves:** Eliminates duplicated consent logic, retry strategies, and LGPD Art. 8 compliance checks spread across every producer service — one service owns all outbound communications.

## Goals

- Deliver email notifications with ≥99% send success rate and p99 dispatch latency < 5s
- Enforce LGPD Art. 8 consent centrally so no producer ever bypasses opt-out preferences
- Guarantee zero duplicate sends via atomic DynamoDB conditional writes (not read-then-write)
- Keep consumer lag < 30s sustained and DLQ rate < 0.5%
- Provide 100% IaC (Terraform + Helm) and a verified load test before v1.0.0 tag

## Tech Stack

**Core:**

- Framework: .NET Minimal APIs (10)
- Language: C# 13
- Database: AWS DynamoDB (single-table, notification log + consent)

**Key dependencies:**

- AWS SES — email delivery
- Apache Kafka — event intake (NotificationRequested consumer as IHostedService)
- .NET Aspire — local orchestration + ServiceDefaults
- Polly — circuit breaker + retry policies
- Scriban (or Razor Class Library) — server-side template rendering
- OpenTelemetry + Serilog — observability
- Terraform + Helm — infrastructure as code

**Supporting:**

- AWS Secrets Manager + KMS — secrets at rest
- LocalStack — local AWS emulation (DynamoDB, SES, SecretsManager, KMS)
- Testcontainers — integration tests
- GitHub Actions — CI/CD with OWASP + Trivy gates

## Scope

**v1 includes:**

- Email-only channel via AWS SES (channel enum is multi-value ready; only Email implemented)
- Kafka consumer (`NotificationRequestedConsumer`) as `IHostedService` in the same API host (ADR-C06)
- LGPD consent management: opt-in/opt-out per channel, enforced before every send (ADR-C04)
- Template rendering with payload validation (AssetApprovedEmail, AssetRejectedEmail, GenericNotificationEmail)
- Outbox-style status lifecycle: Pending → Rendering → Dispatching → Sent | Failed | Suppressed (ADR-C07)
- Atomic idempotency via DynamoDB `attribute_not_exists(correlationId)` conditional write (ADR-C08)
- Token-bucket rate limiter + Polly circuit breaker in front of IEmailSender (ADR-C09)
- Retry with exponential backoff for transient errors; non-retryable errors go straight to DLQ (US-C021)
- Reconciliation job for notifications stuck in `Dispatching` (US-C022)
- Delivery status query and consent management HTTP endpoints
- Full Terraform modules + Helm chart (HPA min 2 / max 6)
- SLOs: send success >99%, p99 latency <5s, DLQ rate <0.5%, consumer lag <30s
- Load test validating idempotency + zero throttling under 1,000-event burst

**Explicitly out of scope (v1):**

- SMS channel implementation
- Push notification channel
- SES bounce/complaint feedback loop (webhook processing) — backlog v1.1
- Synchronous HTTP-based notification intake from producer services

## Constraints

- Timeline: ~14–16 working days (~3 weeks), revised up from 10–12 after adding idempotency, throttling, and failure resilience hardening
- Technical: SES sending-rate quota is account-level and shared with identity-api — token bucket must be sized against actual confirmed quota, not assumed
- Compliance: LGPD Art. 8 (consent) + Art. 46 (security) must be satisfied before v1.0.0 tag
- Deployment shape: single deployable — Kafka consumer runs inside the API host, not a separate worker process (ADR-C06)
