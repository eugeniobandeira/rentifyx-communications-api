# State

**Last Updated:** 2026-07-12T15:05:00Z
**Current Work:** Executing E-01 foundation task-by-task (`.specs/features/e01-foundation/tasks.md`). T01–T09, T11 done (scaffold verified, Aspire AppHost+ServiceDefaults wired, Serilog JSON logging, JSON health checks, ErrorOr in Domain, GlobalExceptionHandler production-safe, AWSOptions wired against real dev/sandbox account with fail-fast credential check, AWS dev-account resource requirements documented, Kafka container (KRaft mode) added to AppHost and verified, `ISecretsProvider`/`SecretsProviderOptions` defined in Application layer). Per the Execution Plan, next up are the remaining parallel Phase 2/3 tracks: T13 (CI workflow), T17 (git-secrets hook) — both only depend on T01. T12 (`SecretsManagerProvider` implementation) now unblocked too (depends on T05+T11). Per AD-013, LocalStack scope was narrowed back to automated tests only (T12 rework) — manual dev run keeps using real AWS. Scope also includes marketing campaigns (E-07) and identity-api integration contract (E-08), both spec'd (E-07 also has design+tasks) but Execute not started — both depend on E-02–E-04 domain work landing after E-01.

---

## Recent Decisions (Last 60 days)

### AD-010: Marketing email added to v1 scope as E-07, sequenced last (2026-07-11)

**Decision:** Marketing campaign email (fan-out, unsubscribe, throughput isolation, bounce/complaint feedback) is in v1 scope, not deferred to a separate future feature — but implemented as E-07/E-08 after the transactional core (E-01–E-06) ships, so campaign work never risks the transactional SLOs already hardened in the original plan.
**Reason:** User wants this service usable for marketing from the start rather than re-scoping later, but campaign fan-out is structurally different (one-to-many, different consent channel, public unsubscribe, reputation risk) and must not be bolted onto the single-recipient dispatch path that E-04 hardened.
**Trade-off:** v1 timeline extends beyond the original 14–16 day estimate; exact new estimate pending Design/Tasks phase for E-07.
**Impact:** New feature specs `.specs/features/e07-marketing-campaigns/spec.md`; `Channel` enum gains a `Marketing` value with its own consent semantics (opt-out only, default-deny — see MKT edge cases); separate Kafka topic/consumer group/rate-limit budget from transactional.

### AD-011: identity-api integration is contract-only this cycle, migration deferred (2026-07-11)

**Decision:** Design and document the `NotificationRequested` contract against identity-api's auth-critical use cases (verification, password reset) now (E-08), but do not touch `rentifyx-identity-api` code in this cycle. Migration off its own direct-SES sender happens after communications-api's v1.0.0 has stabilized in production.
**Reason:** identity-api's own SES sending is already working in production; migrating it now would couple two repos' release timelines together before communications-api has proven itself. Locking the contract now avoids a breaking schema change later.
**Trade-off:** Duplicated SES-sending logic between the two services persists until the migration actually happens — must not be forgotten (tracked explicitly, not just implied).
**Impact:** New feature spec `.specs/features/e08-identity-integration/spec.md`; `docs/contracts/notification-requested.md` becomes the canonical schema both services reference; DLQ records for auth-critical templates get a `severity=auth-critical` tag so failures page instead of queueing passively.

### AD-013: LocalStack scope narrowed to automated tests only; manual dev run still uses real AWS (2026-07-12)

**Decision:** Refines AD-012. Manually running the app (`dotnet run --project AppHost`) still targets the real AWS dev/sandbox account via the named credentials profile (T07, already implemented). But automated integration tests (Testcontainers-based, run in CI and locally on-demand) use a LocalStack container instead of hitting the real dev account. Kafka in AppHost remains a local container either way (never affected).
**Reason:** User wants to occasionally spin up the app against real AWS data to validate true end-to-end integration, then tear the resources down — that's a deliberate, occasional, manual action, not something that should happen on every test run. Hitting real AWS on every `dotnet test` is slow, costs money, requires every CI run to have real AWS credentials, and risks tests polluting/depending on dev-account state.
**Trade-off:** Two code paths for AWS client configuration must both be kept correct — real profile-based credentials (manual dev) vs. LocalStack endpoint override (tests). Testcontainers LocalStack behavior can still diverge from real AWS at the margins (the original LocalStack objection from AD-012), but that risk is now confined to test-only scenarios, not dev/prod-shaped runs.
**Impact:** `.specs/features/e01-foundation/tasks.md` T12 reworked — `SecretsManagerProvider` integration tests now run against a LocalStack Secrets Manager container (Testcontainers), not the real dev-account Secrets Manager. Resolves the open CI-credential-strategy todo below (LocalStack needs no real AWS credentials in CI). E-07 F-14's original SNS/SQS-on-LocalStack test plan is correct as originally written — no rework needed after all.

### AD-012: Drop LocalStack — local dev targets a real AWS dev/sandbox account (2026-07-11)

**Decision:** Local development and integration testing do NOT use LocalStack. Instead, the AppHost and API connect to a real AWS dev/sandbox account (DynamoDB, SES, SecretsManager, KMS) via a named AWS credentials profile. Kafka remains a local container in Aspire (unaffected — Kafka has no LocalStack dependency).
**Reason:** User rejected LocalStack outright as providing no value for this workflow. Using the real AWS dev account also removes an entire class of emulation-parity bugs (LocalStack behavior diverging from real AWS) and removes the need for an init script to fabricate tables/identities that would otherwise need to exist for real in staging/prod anyway.
**Trade-off:** Every developer needs real AWS credentials (a named profile) configured locally, and dev-account AWS resources (DynamoDB tables, SES sender identity, Secrets Manager entries) must be provisioned before the app can run — this is currently a manual/deferred step, not automated by any init script. CI integration tests' AWS access strategy (same dev account vs. a dedicated CI IAM identity) is still an open decision (see Todos).
**Impact:** Supersedes the LocalStack container/init-script portions of E-01 F-01 (T07/T08 reworked — see `.specs/features/e01-foundation/tasks.md`). `.specs/features/e01-foundation/spec.md` US-C002 rewritten. E-07's SNS/SQS-on-LocalStack plan (F-14) also needs rework when E-07 execution starts — flagged, not yet rewritten task-by-task. README/PROJECT.md/ROADMAP.md updated to remove LocalStack from the tech stack and running-locally instructions.

---

### AD-001: ADR-C01 — Kafka-driven event intake, not synchronous HTTP (2026-07-03)

**Decision:** Notification requests are published as `NotificationRequested` Kafka events by producers; this service consumes them asynchronously.
**Reason:** Decouples producers from delivery timing and retry logic; matches the DynamoDB Streams event pattern already used in identity-api.
**Trade-off:** Producers lose synchronous confirmation of delivery — they only know the event was accepted, not that the email was sent.
**Impact:** No REST intake endpoint for notifications in v1; all dispatch flows through the Kafka consumer.

### AD-002: ADR-C02 — Channel-agnostic event schema from day one (2026-07-03)

**Decision:** `NotificationRequested` event includes a `channel` field and the `Channel` enum has `Sms`/`Push` reserved values even though only `Email` is implemented in v1.
**Reason:** Avoids a breaking schema change when SMS (likely leasing-api) or push is added later.
**Trade-off:** Slightly more ceremony in the domain model for unused enum values.
**Impact:** All consumer implementations must handle unknown channel values gracefully (route to DLQ, not crash).

### AD-003: ADR-C03 — Reuse SesEmailSender/MockEmailSender pattern from identity-api (2026-07-03)

**Decision:** Port and generalize the `SesEmailSender` from identity-api rather than building a new SES abstraction.
**Reason:** Proven pattern already in production; reduces net-new risk.
**Trade-off:** Creates a soft dependency on identity-api's implementation decisions — divergence must be tracked.
**Impact:** Infrastructure layer reuses the same `IEmailSender` abstraction shape.

### AD-004: ADR-C04 — Consent check inside communications-api, not trusted from producer (2026-07-03)

**Decision:** Before every send, this service checks `IConsentRepository` — it never trusts a producer claim that consent exists.
**Reason:** Centralizes LGPD Art. 8 compliance; prevents a misconfigured producer from sending to opted-out users.
**Trade-off:** Every dispatch incurs a DynamoDB read for consent.
**Impact:** `DispatchNotificationHandler` always checks consent before calling `IEmailSender`.

### AD-005: ADR-C05 — Server-side template rendering (Scriban or Razor Class Library) (2026-07-03)

**Decision:** Templates are rendered server-side; client/producer does not control rendering.
**Reason:** Keeps templates versioned and testable; avoids exposing template internals via API or event schema.
**Trade-off:** Template changes require a service deployment, not just a content update.
**Impact:** `ITemplateRenderer` / `ScribanTemplateRenderer` own the render pipeline; producers only supply payload data.

### AD-006: ADR-C06 — Kafka consumer as IHostedService inside the API host (2026-07-03)

**Decision:** `NotificationRequestedConsumer` runs as `IHostedService` in the same process as the Minimal API host.
**Reason:** Mirrors identity-api's `OutboxPublisher` pattern; single Helm chart, shared health checks and observability, no operational duplication.
**Trade-off:** Consumer and HTTP traffic scale together — if consumer load needs independent scaling, this decision must be revisited.
**Impact:** `Program.cs` registers the consumer as a hosted service; `StopAsync` must drain in-flight messages before shutdown.

### AD-007: ADR-C07 — Outbox-style persist-before-send lifecycle (2026-07-03)

**Decision:** Notification record is written with `Pending` status before SES is called; status flips to `Sent` only after SES confirms.
**Reason:** Prevents "sent but crashed before marking sent → retried → double-sent" failure.
**Trade-off:** Requires a reconciliation job (US-C022) to resolve records stuck in `Dispatching` after a crash.
**Impact:** Full status enum: Pending → Rendering → Dispatching → Sent | Failed | Suppressed; the `Dispatching` state is what enables crash recovery without duplicates.

### AD-008: ADR-C08 — Atomic idempotency via DynamoDB conditional write (2026-07-03)

**Decision:** `INotificationRepository.SaveIfNotExists` uses DynamoDB `PutItem` with `ConditionExpression: attribute_not_exists(correlationId)`.
**Reason:** Read-then-write has a race window during Kafka consumer rebalance — two consumers can both pass the read check before either writes.
**Trade-off:** DynamoDB conditional writes have cost implications under very high throughput — acceptable for notification volumes.
**Impact:** A duplicate `correlationId` write fails the condition; the handler treats this as success (ack the message, no reprocessing).

### AD-009: ADR-C09 — Local token-bucket rate limiter + Polly circuit breaker in front of IEmailSender (2026-07-03)

**Decision:** A token-bucket limiter (sized below account SES sending-rate quota) and a Polly circuit breaker wrap `IEmailSender`.
**Reason:** Prevents self-inflicted SES `Throttling` cascades during bursts (e.g., bulk asset approvals); circuit breaker fails fast when SES itself is degraded.
**Trade-off:** Token bucket quota must be sized against the actual confirmed SES quota shared with identity-api — do not guess.
**Impact:** `US-C019` implements the limiter; `US-C020` implements the circuit breaker; `US-C032` load test validates both hold under 1,000-event burst.

---

## Active Blockers

### B-001: SES account sending-rate quota unconfirmed (2026-07-03)

**Discovered:** 2026-07-03
**Impact:** Token-bucket limiter (US-C019) cannot be correctly sized until the actual SES quota is known; shared with identity-api, so joint load matters.
**Workaround:** Use a conservative placeholder (e.g., 14 emails/sec) during development; do not finalize before confirming actual quota.
**Resolution:** Confirm the account's SES sending quota via AWS console or `aws ses get-send-quota` before implementing US-C019.

---

## Lessons Learned

*(none yet — project just initialized)*

---

## Quick Tasks Completed

| #   | Description | Date | Commit | Status |
| --- | ----------- | ---- | ------ | ------ |
| —   | —           | —    | —      | —      |

---

## Deferred Ideas

- [ ] Event contract versioning ADR — needs to be written before leasing-api integration begins. Captured during: project initialization.
- [ ] Token-bucket resizing strategy across transactional + campaign + identity-api load once all three are live. Captured during: project initialization; updated 2026-07-11 to include campaign traffic.
- [ ] identity-api code migration off its own `SesEmailSender` (publish `NotificationRequested` instead) — trigger: communications-api v1.0.0 stable in production for an agreed window. Owner: write the migration ADR in `rentifyx-identity-api`'s own `.specs/`, not here — this repo only owns the contract side (E-08). Captured during: 2026-07-11 scope discussion.
- [ ] Campaign creation/management UI or admin API — only if manually publishing `CampaignRequested` events proves insufficient in practice. Captured during: E-07 scoping, 2026-07-11.
- [x] SES bounce/complaint webhook processing — was v1.1 backlog, promoted into v1 scope as E-07 F-14 (MKT-04) since marketing volume makes reputation risk real. Captured during: project initialization; resolved into roadmap 2026-07-11.

---

## Todos

- [ ] Confirm actual AWS SES sending-rate quota (account-level) before implementing US-C019 token bucket — resolves B-001.
- [ ] Decide on Scriban vs. Razor Class Library for template rendering (ADR-C05) — currently open choice, both are valid.
- [x] Verify identity-api's Kafka broker config is reusable for local dev, or set up standalone Kafka container in AppHost (US-C002) — resolved by T09: standalone `AddKafka("kafka")` in AppHost, KRaft mode.
- [ ] `AppHostTests` (T04's health-check test + T09's Kafka test) boots the real API process, not LocalStack — it hits T07's fail-fast `AWS:Profile` check same as a manual run. Works locally once `dotnet user-secrets set "AWS:Profile" "<profile>"` is set on the Api project, but CI has no real AWS profile — this suite will fail in CI (T13) until resolved (e.g. a dedicated CI-only AWS identity, or point `AppHostTests` at LocalStack too). Discovered 2026-07-12 while verifying T09.

---

## Preferences

**Model Guidance Shown:** never
