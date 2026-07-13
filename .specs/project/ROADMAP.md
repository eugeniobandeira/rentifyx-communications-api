# Roadmap

**Current Milestone:** E-03 — Application Layer (Use Cases)
**Status:** E-01 and E-02 complete; E-03 not started (needs Specify)

---

## E-01 · Project Foundation & DevSecOps Pipeline

**Goal:** Repo scaffold, local dev environment (Aspire + real AWS dev/sandbox account + Kafka), CI/CD pipeline with security gates, secrets loading from Secrets Manager — all wired before domain work begins.
**Target:** Day 1

**2026-07-11 update (AD-012):** LocalStack dropped from local dev — the AppHost/API connect to a real AWS dev/sandbox account (DynamoDB, SES, SecretsManager, KMS) via a named credentials profile instead. Kafka is unaffected (still a local Aspire container). See `.specs/project/STATE.md` AD-012.

### Features

**F-01 · Repo & Solution Structure** — DONE (except one permanent manual step: dev-account resource provisioning)

- Clean solution scaffold via `dotnet new clean-arch` (API, Application, Domain, Infrastructure, Tests layers) — ✅ done
- Aspire AppHost + ServiceDefaults, Serilog, CorrelationId, GlobalExceptionHandler, Scalar UI, ErrorOr\<T\> — ✅ done
- AWS SDK configured against a real dev/sandbox account (DynamoDB, SES, SecretsManager, KMS) via named credentials profile + Kafka container in AppHost — ✅ done, reworked per AD-012
- Dev-account resource provisioning (DynamoDB tables `notifications`/`delivery-log`, SES verified sender identity, Secrets Manager entries) — manual for now, not automated; tracked as a todo (this one stays "pending" by design, not an oversight)
- `NotificationRequestedConsumer` registered as `IHostedService` with graceful stop/drain — ✅ done (skeleton only, no message processing — that's E-03)

**F-02 · CI/CD Pipeline & DevSecOps Baseline** — DONE

- GitHub Actions: build → test → coverage gate ≥80% → OWASP dependency-check → Trivy scan — ✅ done (coverage gate is real but currently red at ~5.6% repo coverage; OWASP check needs an `NVD_API_KEY` repo secret added before it actually runs — see STATE.md Todos)
- Branch protection: CI green (build-test-coverage, trivy-scan, owasp-check) + 1 PR review required, force-push/deletion disabled — ✅ done 2026-07-13
- git-secrets pre-commit hook + `ISecretsProvider` abstraction loading SES ARN + Kafka credentials from Secrets Manager — ✅ done

---

## E-02 · Domain Model — Notification & Consent

**Goal:** Pure domain layer with zero framework or AWS references — Notification aggregate, status lifecycle, consent rules, channel-agnostic contracts, 100% unit tested.
**Target:** Day 2–3
**Status:** DONE (2026-07-13) — spec/design/tasks in `.specs/features/e02-domain-model/`, 29 unit tests passing on branch `feat/e02-domain-model`

**2026-07-13 decision:** Default consent for transactional channels (Email in v1) is opt-in absent a record — `ConsentDecision.NoRecordFound()` never suppresses. A `ConsentPreference` record only suppresses when it explicitly says `OptedIn = false`. Marketing (E-07) will use the opposite default (opt-out, default-deny) — different epic, different rule, not a contradiction.

### Features

**F-03 · Notification Aggregate & Value Objects** — DONE

- `Notification` aggregate root with full status enum: Pending → Rendering → Dispatching → Sent | Failed | Suppressed — ✅ done
- `Channel` enum (Email implemented; Sms/Push reserved), `NotificationStatus`, `EmailAddress` VO, `TemplateId` VO — ✅ done
- Domain events: `NotificationDispatched`, `NotificationDelivered`, `NotificationFailed`, `NotificationSuppressed` — ✅ done

**F-04 · Consent & Domain Contracts** — DONE

- `ConsentPreference` VO (Channel, OptedIn, UpdatedAt) + `IConsentRepository` — ✅ done
- `INotificationRepository` (SaveIfNotExists atomic, GetById, GetByRecipient, UpdateStatus) — ✅ done (contract only, DynamoDB implementation is E-04)
- `ITemplateRenderer` + `IEmailSender` contracts — ✅ done
- Domain rule: `Notification.Dispatch()` returns error/suppresses if recipient opted out — ✅ done
- ADRs: C02 (channel-agnostic contract), C04 (consent inside domain), C07 (outbox lifecycle) — reflected in design.md Tech Decisions

---

## E-03 · Application Layer — Use Cases

**Goal:** Kafka consumer → atomic idempotency → consent check → template render → outbox dispatch sequence, fully handler-driven with unit + integration tests.
**Target:** Day 4–6

### Features

**F-05 · Notification Intake (Kafka Consumer)** — PLANNED

- `NotificationRequested` Kafka message contract → `DispatchNotificationHandler`
- Atomic idempotency: `SaveIfNotExists` with `attribute_not_exists(correlationId)` — duplicate = ack, not error
- `ScribanTemplateRenderer` with payload validation against expected template fields

**F-06 · Consent Enforcement & Dispatch Orchestration** — PLANNED

- `DispatchNotificationHandler` full outbox sequence: SaveIfNotExists(Pending) → render → UpdateStatus(Dispatching) → Send → UpdateStatus(Sent|Failed)
- Opted-out path: raise `NotificationSuppressed`, persist Suppressed status, skip SES entirely
- Reconciliation hook for crash-mid-sequence scenario (feeds into US-C022)

---

## E-04 · Infrastructure — SES, DynamoDB, Idempotency & Resilience

**Goal:** SES and DynamoDB implementations, atomic idempotency, token-bucket rate limiter, circuit breaker, DLQ routing, reconciliation job, consumer lag observability.
**Target:** Day 7–11

### Features

**F-07 · SES Integration** — PLANNED

- `SesEmailSender` (port from identity-api, generalized) + `MockEmailSender` for local/test
- `DynamoDbNotificationRepository`: SaveIfNotExists, GetById, GetByRecipient, UpdateStatus
- Single-table design: PK=`NOTIF#{id}`, GSI1=`RECIPIENT#{recipientId}`, GSI2=`CORRELATION#{correlationId}`
- DynamoDB TTL: 90-day auto-expiry on notification records (LGPD Art. 46 data minimization)
- Integration tests for SES + DynamoDB (including conditional-write race) run against the real AWS dev/sandbox account — no LocalStack (AD-012); CI credential strategy still to be decided (see STATE.md Todos)

**F-08 · Throttling & Circuit Breaking** — PLANNED

- Token-bucket rate limiter in front of `IEmailSender`, sized below actual SES account quota
- Polly circuit breaker: opens after N consecutive SES failures, failed sends go to DLQ instead of retrying
- Load test: 1,000 notifications burst → zero SES Throttling errors

**F-09 · Reliability — Retry, DLQ, Poison Messages & Reconciliation** — PLANNED

- Retry: 3 attempts with exponential backoff, transient errors only; non-retryable → DLQ immediately
- Failure classification (determines routing, not just "retry vs. not"):
  - **Poison pill** (malformed JSON, missing required field, deserialization error) → straight to DLQ, retry will never resolve it
  - **Transient** (DB unreachable, network timeout, SES throttling) → retry with backoff, most resolve on their own
  - **Business rule** (e.g. recipient opted out, template not found) → not an error at all — handle as a normal domain outcome (`Suppressed`/`Failed` status), never route to DLQ
- Retry topic chain, one topic per delay stage, each with its own consumer that only processes once the delay has elapsed (checked via `x-next-retry-at`): `notification-requested` → `notification-requested-retry-5s` → `-retry-1m` → `-retry-10m` → `notification-requested-dlq`
- DLQ Kafka topic consumer with original payload + failure reason + retry count; required headers on every retry/DLQ message for traceability without needing to reproduce the failure:
  - `x-original-topic` — where the message originally came from
  - `x-retry-count` — attempts so far
  - `x-first-failure-timestamp` — when it first failed
  - `x-exception-type` / `x-exception-message` — for triage without reprocessing
  - `x-next-retry-at` — computed timestamp the retry-topic consumer checks before processing
- Reconciliation `IHostedService`: resolve notifications stuck in `Dispatching` > 2 min
- OTEL metrics: `kafka_consumer_lag_notification_requested`, `notification_dispatch_duration_seconds` histogram

*Header schema and retry-topic-chain convention above sourced from a Kafka/.NET reference guide reviewed 2026-07-13 (`kafka-dotnet-versao-final.html`, a personal study doc, not part of the repo's tracked source) — captured here so the pattern isn't lost before F-09 design starts.*

---

## E-05 · API Layer & LGPD Compliance

**Goal:** Minimal HTTP surface for status queries and consent management, security hardened, auto-documented.
**Target:** Day 12–13

### Features

**F-10 · Status & Consent Endpoints** — PLANNED

- `GET /v1/api/notifications/{id}` and `GET /v1/api/notifications/recipient/{recipientId}`
- `GET /v1/api/consent/{recipientId}` + `PUT /v1/api/consent/{recipientId}` with audit log
- Rate limiting on consent endpoints (prevent enumeration/abuse)
- Security headers: HSTS, X-Content-Type-Options, X-Frame-Options, CSP
- Scalar UI at `/scalar` + AsyncAPI/markdown spec for `NotificationRequested` Kafka contract

---

## E-06 · Infrastructure as Code & Production Readiness

**Goal:** Full Terraform + Helm, SLOs defined, load test passes, OWASP ZAP clean, ADRs finalized, v1.0.0 tagged and deployed to staging.
**Target:** Day 14–16

### Features

**F-11 · Terraform & Kubernetes** — PLANNED

- Terraform: `aws_ses_domain_identity` + DKIM/SPF, `aws_dynamodb_table` with GSI2, `aws_secretsmanager_secret`, IAM IRSA least-privilege role
- Helm: Deployment + Service + HPA (min 2 / max 6) + liveness/readiness probes + PodDisruptionBudget

**F-12 · Observability, Load Testing & Ship Gate** — PLANNED

- SLOs: send success >99%, p99 <5s, DLQ rate <0.5%, consumer lag <30s
- OTEL metrics: `notifications_sent_total`, `notifications_suppressed_total`, `notifications_failed_total`, `kafka_consumer_lag_notification_requested`
- k6 load test: 1,000-event burst → zero Throttling + zero duplicates + p99 SLO holds (staging + real SES sandbox)
- OWASP ZAP scan, PII leak verification in logs, coverage ≥80%
- ADRs C01–C09 finalized in `/docs/adr/`, v1.0.0 tag → ECR → staging deploy

---

## E-07 · Marketing Email Campaigns

**Goal:** Add campaign fan-out (`CampaignRequested` → per-recipient notifications) as an additive flow that reuses SES/template/consent/idempotency building blocks without disturbing transactional SLOs. Public token-based unsubscribe, isolated throughput budget, bounce/complaint auto opt-out.
**Target:** Planned after E-06 (v1 core) stabilizes — added to v1 scope per 2026-07-11 decision, but sequenced last so it doesn't block the transactional ship gate.
**Spec:** `.specs/features/e07-marketing-campaigns/spec.md`

### Features

**F-13 · Campaign Fan-Out & Unsubscribe** — PLANNED

- `CampaignRequested` Kafka contract: `campaignId`, `templateId`, `recipientIds[]`, `payload`
- Per-recipient idempotency key (`campaignId + recipientId`), reuses `IConsentRepository`/`Marketing` channel, reuses outbox lifecycle
- `GET /v1/api/campaigns/{campaignId}` aggregate status endpoint
- Public `GET /v1/api/unsubscribe?token=...` — signed, single-purpose, no auth — reuses consent audit log

**F-14 · Campaign Throughput Isolation & Reputation** — PLANNED

- Separate Kafka topic/consumer group (`campaign-requested`) and separate token-bucket budget from transactional
- SES SNS bounce/complaint feedback consumer → auto opt-out per channel (hard bounce/complaint only, not soft bounce)
- Load test: campaign burst concurrent with steady transactional traffic, transactional SLO must hold
- **2026-07-11 note (AD-012):** F-14's SNS/SQS-for-SES-feedback plan assumed a LocalStack-hosted SNS/SQS stack; now needs rework against the real AWS dev/sandbox account when E-07 execution starts (not yet rewritten task-by-task — see `.specs/features/e07-marketing-campaigns/`)

---

## E-08 · Identity-API Integration Contract

**Goal:** Lock the `NotificationRequested` contract against identity-api's auth-critical use cases (email verification, password reset) now, so identity-api's future migration off its own direct-SES sender is a swap, not a redesign. Contract/ADR only — no code changes to `rentifyx-identity-api` in this cycle (decision: 2026-07-11, migrate after E-06 ships to production).
**Target:** Alongside E-07 spec work; no implementation dependency on E-01–E-06 completing first (this is a documentation/contract deliverable).
**Spec:** `.specs/features/e08-identity-integration/spec.md`

### Features

**F-15 · Auth-Critical Contract & Migration Plan** — PLANNED

- `docs/contracts/notification-requested.md` — canonical schema doc, validated against `EmailVerification`/`PasswordReset` example payloads
- `auth-critical` severity tag on DLQ records for templates flagged as auth-critical (pages instead of passive queue)
- Migration decommission plan: trigger condition (v1.0.0 + stabilization window) and rollback path (feature flag / no dual-active sending) documented for a future identity-api-side ADR

---

## Future Considerations (Post v1)

- SMS channel implementation (likely triggered by leasing-api requirement)
- Push notification channel
- Event contract versioning ADR before leasing-api integration begins
- Token-bucket resizing once shared SES quota with identity-api (and now marketing campaigns) is confirmed under joint load
- identity-api code migration off its own `SesEmailSender` — tracked by E-08 F-15's decommission plan, executes after E-06 stabilizes in production
- Campaign creation/management UI or admin API, if manual `CampaignRequested` publishing proves insufficient
- Segmentation-as-a-service inside communications-api, if producers can't reasonably resolve their own recipient lists
