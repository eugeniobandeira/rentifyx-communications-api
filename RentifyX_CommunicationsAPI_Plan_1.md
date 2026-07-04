# rentifyx-communications-api — Project Plan (v1: Email-Only)

**Stack:** .NET 10 · Minimal APIs · Clean Architecture · DDD · TDD
**Cloud:** AWS SES · DynamoDB · Secrets Manager · KMS
**Infra:** Terraform · .NET Aspire · Docker · GitHub Actions
**Observability:** OpenTelemetry · Serilog · Scalar/ReDoc
**Compliance:** LGPD (Art. 8 consent, Art. 46 security) · OWASP Top 10
**Template:** `dotnet new clean-arch -n RentifyX.Communications`
**Estimated duration:** ~14–16 working days (~3 weeks) — revised up from the original 10–12 day estimate after hardening for idempotency, throttling, and failure resilience (see Revision Note below)
**Deployment shape:** Single deployable — Minimal API host with the Kafka consumer running as an `IHostedService` in the same process (not a separate worker). See ADR-C06.
**Consumers (via Kafka):** identity-api, asset-registry-api, and future leasing-api/billing-api/risk-api

**Scope boundary for v1:** Email only (AWS SES). SMS/push are explicitly deferred — the domain model and event contract are designed to be channel-extensible, but no SMS/push provider integration ships in this version.

---

## Revision Note

The original plan (10–12 days) covered the happy path plus basic retry/DLQ. This revision adds a dedicated reliability/performance layer after gap-checking against three failure modes that matter for a service that will eventually sit in the critical path of every other microservice:

1. **Idempotency wasn't actually atomic** — the original dedup check was a read-then-write, vulnerable to a race during Kafka consumer rebalance.
2. **No SES throttling protection** — a burst of events (e.g., asset-registry approving 1,000 listings at once) could blow through SES's sending-rate quota and cascade into DLQ.
3. **No visibility into consumer lag or poison messages** — a stuck or malformed message had no distinct handling path from a transient failure.

These are now first-class stories in E-04, not afterthoughts. Estimated duration moved from 10–12 to 14–16 days as a result — this is the honest cost of production-grade reliability, not scope creep.

---

## Known Decisions & Watch Items

| ID | Decision | Rationale |
|---|---|---|
| ADR-C01 | Kafka-driven, event-sourced notification requests (not synchronous HTTP calls from other services) | Decouples producers from delivery timing/retries; matches existing DynamoDB Streams pattern from identity-api |
| ADR-C02 | Generic `NotificationRequested` event schema designed for multi-channel from day one, even though only Email is implemented | Avoids a breaking schema change when SMS/push are added later (leasing-api will likely need SMS) |
| ADR-C03 | Reuse `SesEmailSender`/`MockEmailSender` pattern from identity-api rather than reinvent | Proven pattern already tested in production; reduces net-new risk |
| ADR-C04 | Consent/opt-out check happens *inside* communications-api before every send, not trusted from the producer | Centralizes LGPD Art. 8 compliance in one place instead of duplicating the check in every future producer service |
| ADR-C05 | Template rendering engine: server-side (Scriban or Razor Class Library), not client-side | Keeps templates versioned and testable; avoids exposing template internals via API |
| ADR-C06 | Kafka consumer runs as `IHostedService` inside the same API host, not a separate worker deployable | Mirrors identity-api's `OutboxPublisher` pattern; single Helm chart, shared health checks/observability, no operational duplication. Revisit only if consumer load needs to scale independently of HTTP traffic |
| ADR-C07 | Outbox-style persist-before-send: notification record written with status `Pending` *before* calling SES, status flipped to `Sent` only after SES confirms | Prevents the classic "sent but crashed before marking sent → retried → double-sent" failure. Without this, retry safety is cosmetic |
| ADR-C08 | Idempotency enforced via DynamoDB conditional write (`attribute_not_exists`) on `correlationId`, not a read-then-write check | Read-then-write has a race window during consumer rebalance (two consumers can both pass the read check before either writes). Conditional write is atomic at the DB layer |
| ADR-C09 | Local token-bucket rate limiter in front of `IEmailSender`, sized below SES's account sending-rate quota | Prevents self-inflicted `Throttling` cascades during traffic bursts (e.g., bulk asset approvals). Circuit breaker (Polly) wraps the SES call to fail fast and shed load when SES itself is degraded |
| ⚠️ Watch | Event contract versioning strategy | If asset-registry-api and leasing-api both start publishing `NotificationRequested`, a schema change requires coordinated rollout — flag for an ADR before leasing-api integration begins |
| ⚠️ Watch | Bounce/complaint handling (SES feedback loop) | Not in v1 scope, but SES bounce/complaint webhooks will eventually be needed to keep sender reputation healthy — tracked as backlog item for v1.1 |
| ⚠️ Watch | SES sending-rate quota is account-level, shared with identity-api | If both services send heavily at the same time, the token bucket in ADR-C09 needs to account for the shared quota, not just this service's slice — confirm actual SES quota before sizing the limiter |
| Deferred | SMS/push channel implementations | Explicitly out of scope until a real consumer (likely leasing-api) needs them |
| Deferred | Bounce/complaint SES feedback processing | Backlog — v1.1 |

---

## Epic Overview

| Epic | Title | Days | Goal |
|---|---|---|---|
| E-01 | Project Foundation & DevSecOps Pipeline | Day 1 | Template gives you most of Day 1 for free — reuse identity-api's CI/secrets patterns |
| E-02 | Domain Model — Notification & Consent | Day 2–3 | Pure domain: Notification aggregate, Template VO, consent rules, channel-agnostic contracts |
| E-03 | Application Layer — Use Cases | Day 4–6 | Kafka consumer → validate consent → render template → dispatch |
| E-04 | Infrastructure — SES, DynamoDB, Idempotency & Resilience | Day 7–11 | SES integration, atomic idempotency, throttling, circuit breaker, DLQ, consumer lag monitoring |
| E-05 | API Layer & LGPD Compliance | Day 12–13 | Status query endpoints, consent management endpoints, security hardening |
| E-06 | Infrastructure as Code & Production Readiness | Day 14–16 | Terraform, Helm, SLOs, load test, ADRs, v1.0.0 tag |

---

## E-01 · Project Foundation & DevSecOps Pipeline (Day 1)

**Goal:** Template gives you the scaffold for free — focus on wiring SES Secrets Manager entries and Kafka consumer config.

### F-01 · Repo & Solution Structure

**US-C001** — As a dev, I want the clean solution scaffold so I can start coding without friction
- [AUTO] `dotnet new clean-arch -n RentifyX.Communications`
- [AUTO] Solution layers: API, Application, Domain, Infrastructure, Tests
- [AUTO] Aspire AppHost + ServiceDefaults, Serilog, CorrelationId, GlobalExceptionHandler, Scalar UI, ErrorOr\<T\>
- Copy `.editorconfig` (CA5xxx security rules) into repo

**US-C002** — As a dev, I want Kafka + SES + DynamoDB available locally via Aspire
- Add LocalStack container (DynamoDB, SES, SecretsManager, KMS) to AppHost
- Add Kafka container to AppHost (reuse identity-api's broker config if shared cluster, else standalone for local dev)
- LocalStack init script: create DynamoDB tables (notifications, delivery-log), SES verified sender identity
- Validate: `dotnet run --project AppHost` boots all containers cleanly

**US-C003** — As a dev, I want the Kafka consumer registered as a hosted service in the same host (ADR-C06)
- Register `NotificationRequestedConsumer` as `IHostedService` in the API host's `Program.cs` (same pattern as identity-api's `OutboxPublisher`)
- Confirm consumer starts/stops gracefully with the host lifecycle (`StopAsync` drains in-flight message before shutdown)
- Document in README: this is one deployable, not API + worker

### F-02 · CI/CD Pipeline & DevSecOps Baseline

**US-C004** — As a tech lead, I want the same security gates as identity-api so standards stay consistent
- [AUTO] GitHub Actions base workflow: build → test
- Extend CI: coverage gate ≥80%, OWASP dependency-check, Trivy scan
- Branch protection: CI green + 1 PR review required

**US-C005** — As a dev, I want secrets loaded from Secrets Manager, never committed
- git-secrets pre-commit hook (reuse identity-api patterns file)
- `ISecretsProvider` abstraction (copy interface from identity-api, new implementation binding)
- Load SES sender identity ARN + Kafka credentials from Secrets Manager at startup

---

## E-02 · Domain Model — Notification & Consent (Day 2–3)

**Goal:** Pure domain, zero framework/AWS references, channel-agnostic contracts even though only Email ships in v1.

### F-03 · Notification Aggregate & Value Objects

**US-C006** — As a domain expert, I want a Notification aggregate that enforces delivery rules and models the outbox lifecycle
- Define `Notification` aggregate root: Id, RecipientId, Channel, TemplateId, Payload, CorrelationId, Status, CreatedAt, DispatchedAt, SentAt
- Create `Channel` enum: `Email` (only implemented value in v1; `Sms`/`Push` reserved for future)
- Create `NotificationStatus` enum: **Pending → Rendering → Dispatching → Sent | Failed | Suppressed** (the `Pending`→`Dispatching` split is what makes the outbox pattern in ADR-C07 possible — status is persisted *before* the SES call, not after)
- Create `EmailAddress` value object (reuse validation logic pattern from identity-api's `Email` VO)
- Create `TemplateId` value object with allowed-template validation

**US-C007** — As a dev, I want domain events so delivery outcomes are observable
- Define `NotificationDispatched`, `NotificationDelivered`, `NotificationFailed`, `NotificationSuppressed` domain events
- Add `RaiseDomainEvent()` to `AggregateRoot` base (reuse from identity-api)

### F-04 · Consent & Domain Contracts

**US-C008** — As an LGPD officer, I want consent enforced at the domain level, not just the API
- Define `ConsentPreference` value object: Channel, OptedIn, UpdatedAt
- Define `IConsentRepository`: GetPreference(recipientId, channel), Save
- Domain rule: `Notification.Dispatch()` returns an error if recipient has opted out of the channel

**US-C009** — As a dev, I want repository/service contracts defined before infrastructure exists
- Define `INotificationRepository`: `SaveIfNotExists` (atomic conditional write, see ADR-C08), GetById, GetByRecipient, UpdateStatus
- Define `ITemplateRenderer`: Render(templateId, payload) → RenderedContent
- Define `IEmailSender`: Send(EmailAddress, subject, body) → DeliveryResult

**US-C010** — As a dev, I want a fully unit-tested domain layer
- Unit tests: `EmailAddress` VO — valid/invalid formats
- Unit tests: `Notification` aggregate — full status transition graph including `Dispatching` state, opt-out suppression, event emission
- Unit tests: `ConsentPreference` — default opt-in/opt-out rules per LGPD

**US-C011** — ADRs for domain decisions
- ADR-C02: channel-agnostic event contract rationale
- ADR-C04: consent check inside domain, not trusted from producer
- ADR-C07: outbox status lifecycle rationale
- Review: zero framework/AWS references in Domain layer

---

## E-03 · Application Layer — Use Cases (Day 4–6)

**Goal:** Kafka consumer intake → atomic idempotency check → consent check → template render → dispatch, fully handler-pattern driven.

### F-05 · Notification Intake (Kafka Consumer)

**US-C012** — As a producing service, I want to request a notification by publishing an event, not calling an API synchronously
- [AUTO] Feature folder: `Application/Features/Notifications/Dispatch/`
- Define `NotificationRequested` Kafka message contract (recipientId, channel, templateId, payload, correlationId)
- Implement Kafka consumer (`NotificationRequestedConsumer`) → maps to `DispatchNotificationHandler`
- Unit tests: consumer message mapping and malformed-payload rejection path

**US-C013** — As a dev, I want idempotency enforced atomically, not via a race-prone read-then-write (ADR-C08)
- `INotificationRepository.SaveIfNotExists`: DynamoDB `PutItem` with `ConditionExpression: attribute_not_exists(correlationId)`
- On condition failure (duplicate): log and ack the Kafka message without reprocessing — this is a *success* path, not an error
- Unit + integration tests: two concurrent handlers racing on the same `correlationId` → exactly one succeeds in creating the record
- Explicitly test the rebalance scenario: simulate two consumer instances receiving the same message (at-least-once delivery) and confirm only one dispatch occurs

**US-C014** — As a dev, I want template rendering isolated and testable
- Implement `ScribanTemplateRenderer` (or Razor-based, per ADR-C05)
- Define initial templates: `AssetApprovedEmail`, `AssetRejectedEmail` (asset-registry-api), generic `GenericNotificationEmail` fallback
- Validate payload against template's expected fields before render (fail fast, don't silently drop fields)
- Unit tests: rendering with valid/missing/extra payload fields

### F-06 · Consent Enforcement & Dispatch Orchestration

**US-C015** — As an LGPD officer, I want opted-out recipients never emailed
- `DispatchNotificationHandler`: check `IConsentRepository` before calling `IEmailSender`
- On opt-out: raise `NotificationSuppressed`, persist status, do not call SES
- Unit tests: opted-in success path, opted-out suppression path

**US-C016** — As a dev, I want the dispatch handler to follow the full outbox sequence, not fire-and-forget
- Handler sequence: `SaveIfNotExists (status=Pending)` → render → `UpdateStatus(Dispatching)` → call `IEmailSender` → `UpdateStatus(Sent | Failed)`
- If the process crashes between `Dispatching` and the final status update, a reconciliation job (US-C019) picks up stuck records rather than the consumer blindly retrying
- Unit tests: full sequence success path, crash-simulated mid-sequence path

---

## E-04 · Infrastructure — SES, DynamoDB, Idempotency & Resilience (Day 7–11)

**Goal:** Reuse identity-api's proven SES pattern; add atomic idempotency, throttling protection, circuit breaking, and observability into failure modes — not just retry-and-hope.

### F-07 · SES Integration

**US-C017** — As a dev, I want SES sending abstracted and reusable across templates
- Implement `SesEmailSender` (port forward from identity-api's implementation, generalized for arbitrary rendered content)
- `MockEmailSender` for local/test environments
- Testcontainers.LocalStack integration test for SES send path

**US-C018** — As a dev, I want a DynamoDB-backed notification log so delivery status is queryable
- Implement `DynamoDbNotificationRepository`: `SaveIfNotExists`, GetById, GetByRecipient, UpdateStatus
- Single-table design: PK=`NOTIF#{id}`, GSI1=`RECIPIENT#{recipientId}`, GSI2=`CORRELATION#{correlationId}` for the idempotency lookup
- DynamoDB TTL on notification records: auto-expire after 90 days (data minimization, LGPD Art. 46)
- Testcontainers.DynamoDb integration tests for all repository methods, including the conditional-write race test from US-C013
- ADR: single-table design rationale (mirrors ADR-005 from identity-api)

### F-08 · Throttling & Circuit Breaking (ADR-C09)

**US-C019** — As a dev, I want a local rate limiter so bursts never blow through SES's sending quota
- Implement a token-bucket limiter in front of `IEmailSender`, sized conservatively below the account's SES sending-rate quota (confirm actual quota per the Watch Item — do not guess)
- Requests exceeding the bucket queue briefly (bounded) rather than firing and hitting `Throttling` from SES
- Load test: simulate 1,000 notifications enqueued at once (e.g., bulk asset approval) and confirm zero `Throttling` errors from SES
- Unit tests: bucket refill behavior, bounded queue overflow handling

**US-C020** — As a dev, I want a circuit breaker around SES so a degraded SES doesn't take down the consumer
- Wrap `IEmailSender` calls with Polly circuit breaker: opens after N consecutive failures, half-opens after a cooldown
- When circuit is open: notifications go straight to `Failed` status + DLQ instead of consuming retry attempts against a known-down dependency
- Integration test: forced consecutive SES failures → circuit opens → subsequent calls short-circuit without hitting SES

### F-09 · Reliability — Retry, DLQ, Poison Messages & Reconciliation

**US-C021** — As a dev, I want failed sends retried without duplicate delivery, and poison messages isolated
- Retry policy: 3 attempts with exponential backoff, but **only for errors classified as transient** (SES `Throttling`, network timeout)
- Non-retryable errors (malformed payload, unknown template, invalid recipient) skip retry entirely and go straight to DLQ — don't burn 3 attempts on something that will never succeed
- Implement DLQ Kafka topic consumer for failed notifications (for manual review/replay tooling)
- Dead-letter record includes original payload + failure reason + retry count for debuggability
- Unit tests: transient vs. non-retryable error classification, DLQ routing for each

**US-C022** — As a dev, I want a reconciliation job so notifications stuck in `Dispatching` (crash mid-sequence) get resolved
- Scheduled `IHostedService` job: query notifications in `Dispatching` status older than a threshold (e.g., 2 minutes)
- For each: check with SES (or assume failed if unconfirmable) and resolve to `Sent` or `Failed`, never leave permanently stuck
- Integration test: manually force a record into `Dispatching` and confirm the reconciliation job resolves it

**US-C023** — As an SRE, I want consumer lag visible so a backlog is caught before it becomes a user-facing delay
- Emit custom OTEL metric: `kafka_consumer_lag_notification_requested`
- Emit `notification_dispatch_duration_seconds` histogram for p50/p99 tracking
- Alert: PagerDuty trigger if consumer lag exceeds threshold for 5min sustained

**US-C024** — As a security engineer, I want SES sender identity and Kafka credentials loaded securely
- `SecretsManagerConfigurationProvider`: load SES ARN, Kafka SASL credentials at startup
- Secret caching (5min TTL)
- Integration test: service boots with secrets, fails gracefully on missing secrets

---

## E-05 · API Layer & LGPD Compliance (Day 12–13)

**Goal:** Minimal query/management surface — this service is primarily event-driven, not request/response.

### F-10 · Status & Consent Endpoints

**US-C025** — As a client service, I want to query delivery status for a notification
- [AUTO] Endpoint auto-registration via IEndpoint reflection
- `GET /v1/api/notifications/{id}` → delivery status, timestamps
- `GET /v1/api/notifications/recipient/{recipientId}` → notification history for a recipient

**US-C026** — As an LGPD officer, I want consent management endpoints so users can opt out of communication channels
- `GET /v1/api/consent/{recipientId}` → current preferences per channel
- `PUT /v1/api/consent/{recipientId}` → update opt-in/opt-out (Art. 8)
- Audit log: every consent change recorded with recipientId + timestamp + previous/new value

**US-C027** — As a security engineer, I want the same hardened middleware baseline as identity-api
- [AUTO] GlobalExceptionHandler, CorrelationId middleware
- Rate limiting middleware on consent endpoints (prevent opt-out abuse/enumeration)
- Security headers: HSTS, X-Content-Type-Options, X-Frame-Options, CSP

**US-C028** — As a dev, I want auto-generated API docs
- [AUTO] Scalar UI at `/scalar` with OpenAPI 3.1 schema
- Document the `NotificationRequested` Kafka contract separately (AsyncAPI or markdown spec, since it's not HTTP)

---

## E-06 · Infrastructure as Code & Production Readiness (Day 14–16)

**Goal:** Terraform, Helm, SLOs, load-tested, ADR set finalized, v1.0.0 tagged.

### F-11 · Terraform & Kubernetes

**US-C029** — As a DevOps engineer, I want 100% IaC for communications infra
- Terraform module: `aws_ses_domain_identity` + DKIM/SPF verification
- Terraform module: `aws_dynamodb_table` (notifications, DLQ metadata) with GSI2 for correlation lookup
- Terraform module: `aws_secretsmanager_secret` (SES ARN, Kafka credentials)
- Terraform: IAM least-privilege role for EKS service account (IRSA), scoped to SES send + DynamoDB table only

**US-C030** — As a DevOps engineer, I want Helm charts for one-command deploy
- Deployment, Service, HPA (min 2 / max 6 replicas)
- Liveness/readiness probes, resource limits, PodDisruptionBudget

### F-12 · Observability, Load Testing & Ship Gate

**US-C031** — As a dev, I want SLOs so degraded delivery is caught before it becomes a support ticket
- Define SLOs: send success rate > 99%, p99 dispatch latency < 5s, DLQ rate < 0.5%, consumer lag < 30s sustained
- Custom OTEL metrics: `notifications_sent_total`, `notifications_suppressed_total`, `notifications_failed_total`, `kafka_consumer_lag_notification_requested`
- Alert: PagerDuty trigger if DLQ rate > 1% for 10min

**US-C032** — As a tech lead, I want a load test validating the resilience work actually holds under burst traffic
- k6 (or similar) load test: simulate 1,000 `NotificationRequested` events published in a burst (mirrors asset-registry bulk-approval scenario)
- Assert: zero SES `Throttling` errors (validates US-C019 token bucket), zero duplicate sends (validates US-C013 idempotency), p99 dispatch latency SLO holds
- Run against a staging-like environment with real SES sandbox, not just LocalStack

**US-C033** — As a tech lead, I want a final security/compliance review before v1.0.0
- OWASP ZAP scan on the (small) HTTP surface
- Verify: consent enforcement cannot be bypassed via malformed Kafka payloads
- Verify: no PII (email addresses, payload contents) leaked in logs
- Finalize ADRs C01–C09, cross-link in `/docs/adr/`
- Coverage run ≥80%
- Tag v1.0.0 → push image to ECR → trigger staging deploy

---

## Gap Analysis (self-check before finalizing)

| Gap identified | Resolution in plan |
|---|---|
| How do producer services agree on template names/payload shape? | US-C014 payload validation + separate AsyncAPI-style contract doc in US-C028 |
| What stops a replayed/rebalanced Kafka message from double-sending? | Atomic conditional write in US-C013 (ADR-C08), not a read-then-write check |
| What if the process crashes between "sent" and "marked sent"? | Outbox-style status lifecycle (ADR-C07) + reconciliation job in US-C022 |
| What stops a traffic burst from triggering SES throttling cascades? | Local token-bucket limiter in US-C019, validated by load test in US-C032 |
| What happens when SES itself is degraded, not just rate-limited? | Circuit breaker in US-C020 fails fast instead of retry-storming a down dependency |
| Does every failure burn the same 3 retries, even unfixable ones? | Transient vs. non-retryable error classification in US-C021 |
| How do we know if the consumer is falling behind before users notice? | Consumer lag metric + alert in US-C023 |
| What happens to PII in notification payloads after send? | DynamoDB TTL (90 days) in US-C018, no plaintext payload retained beyond that |
| Can a bad actor spam opt-out changes to harass a user? | Rate limiting on consent endpoints in US-C027 |
| Is there a path to add SMS without a breaking change? | Channel-agnostic event contract (ADR-C02) + `Channel` enum already models multiple values |
| Is the reliability work actually validated, or just assumed? | Dedicated load test (US-C032) before v1.0.0 ship gate, not just unit tests |
| What if SES fails silently (bounce, not exception)? | Explicitly flagged as **out of scope / v1.1 backlog** (SES feedback loop) — not solved in v1 |

---

*Generated from RentifyX planning conventions: epics → features → user stories → tasks, template-aware, LGPD-first, gap-analyzed, reliability-validated.*
