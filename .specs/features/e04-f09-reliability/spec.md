# E-04 · F-09 — Reliability: Retry, DLQ, Poison Messages & Reconciliation Specification

## Problem Statement

`NotificationRequestedConsumer` (E-03) always commits the Kafka offset regardless of outcome — a deliberate decision made because no DLQ existed yet and blocking the partition on every failure was judged worse. This means today, a transient failure (DynamoDB unreachable, SES throttled) is logged and silently dropped — never retried, never recoverable. A malformed message (poison pill) gets the same treatment. And a crash between `UpdateStatusAsync(Dispatching)` and the SES call leaves a notification stuck in `Dispatching` forever, with nothing watching for it. F-09 closes all three gaps: real retry with backoff for transient failures, a DLQ for what retry can't fix, and a reconciliation job for crash-induced stuck records.

## Goals

- [ ] Every dispatch failure is classified (poison pill / transient / business rule) and routed accordingly — nothing is silently dropped
- [ ] Transient failures get 3 retry attempts with exponential backoff (5s / 1m / 10m) before landing in the DLQ
- [ ] The original consumer never blocks its partition — it always commits, routing failures forward instead of retrying in place
- [ ] Notifications stuck in `Dispatching` for more than 2 minutes (crash recovery) are detected and given another real chance at delivery

## Out of Scope

| Feature | Reason |
| --- | --- |
| A UI, API endpoint, or admin tooling to inspect/replay DLQ messages | Not in v1 scope per `.specs/project/ROADMAP.md` — "Campaign creation/management UI or admin API" and equivalent tooling for DLQ are explicitly deferred (Future Considerations). F-09 only ensures messages land in the DLQ topic with enough context to triage manually. |
| SES bounce/complaint feedback handling | E-07 F-14's concern (marketing), not transactional reliability |
| Retrying business-rule outcomes (opted-out, suppressed) | These are not errors — `Notification.Dispatch()` already returns a normal `Suppressed`/`Failed` domain outcome for them (AD-014), and F-09's classification explicitly never routes them to retry/DLQ |
| Confirming the real SES sending-rate quota | B-001, tracked separately — F-09's retry backoff timings are fixed regardless of B-001's resolution |
| Changing F-08's rate limiter/circuit breaker behavior | F-08 already ships; F-09 only decides what happens to a notification *after* `ResilientEmailSender` returns a failure — it doesn't touch F-08's internals |

---

## User Stories

### P1: Failure classification ⭐ MVP

**User Story**: As the system, I want every dispatch failure classified as poison pill, transient, or business rule, so that retry/DLQ routing decisions are consistent and never guessed ad hoc per call site.

**Why P1**: Every other story in this spec depends on this classification existing first — retry routing, DLQ routing, and header content all read from it.

**Classification table** (by `ErrorOr` error code or caught exception type — confirmed against the actual codebase, not assumed):

| Source | Code / Exception | Classification | Reason |
| --- | --- | --- | --- |
| Consumer deserialization | `JsonException` (malformed JSON, null after deserialize) | Poison pill | Never resolves itself on retry |
| `DispatchNotificationValidator` | Any `FluentValidation` failure | Poison pill | The message itself is malformed — retry won't fix a bad payload |
| `EmailAddress.Create` / `TemplateId.Create` / `NotificationEntity.Create` | `Notification.Invalid*` codes | Poison pill | Same — structurally invalid data in the message |
| `ScribanTemplateRenderer.RenderAsync` | `Template.NotFound` | Poison pill | **Correction (2026-07-14, confirmed with the user):** ROADMAP originally listed "template not found" as a business-rule outcome (like opt-out) — reclassified. A missing template is a deploy/config bug, not a legitimate business outcome; it will never resolve on retry and needs human intervention. Consistent with `Template.MissingField`/`Template.ParseError` below, which share the same error-code family. |
| `ScribanTemplateRenderer.RenderAsync` | `Template.MissingField` | Poison pill | The producer sent a payload missing a field the template requires — a producer bug, not transient |
| `ScribanTemplateRenderer.RenderAsync` | `Template.ParseError` | Poison pill | The template source itself is malformed — a deploy-time bug |
| `SesEmailSender.SendAsync` | `Ses.SendFailed` | Transient | SES-side failure (throttling, transient AWS error) — matches ROADMAP's "SES throttling" example |
| `ResilientEmailSender.SendAsync` | `Resilience.RateLimitExceeded` | Transient | Self-inflicted throttling (F-08) — resolves once the queue drains |
| `ResilientEmailSender.SendAsync` | `Resilience.CircuitOpen` | Transient | SES is degraded right now — resolves once the circuit closes |
| Repository calls (`INotificationRepository`, `IConsentRepository`) | Unhandled exception (e.g. DynamoDB throttling/unavailable) | Transient | Matches ROADMAP's "DB unreachable" example |
| `Notification.Dispatch()` returning `Suppressed` | n/a — not an error, a normal `ErrorOr` success value | Business rule | AD-014 — opted-out recipient. Never enters classification at all; already handled as a terminal status by the existing handler, untouched by F-09. |

**Acceptance Criteria**:

1. WHEN a failure's error code/exception type matches the classification table THEN the system SHALL route it exactly as that row specifies
2. WHEN a failure's error code/exception type does NOT match any row in the table (an unanticipated case) THEN the system SHALL default to Poison Pill (fail closed — an unclassified failure must not loop through retry indefinitely; it must be visible in the DLQ for a human to triage and add a proper classification)
3. WHEN `Notification.Dispatch()` returns a normal `Suppressed`/`Sent` outcome (not an `ErrorOr` error) THEN classification SHALL NOT be invoked at all — this path is unchanged from E-03/F-07

**Independent Test**: For each row in the classification table, trigger that exact failure in a test and assert the resulting routing decision (retry-chain publish vs. DLQ publish) matches the table.

---

### P1: Retry topic chain with exponential backoff ⭐ MVP

**User Story**: As the system, I want a transient failure retried 3 times with exponential backoff (5s, 1m, 10m) via a dedicated Kafka topic per delay stage, so that most transient failures resolve themselves without needing a DLQ or human intervention.

**Why P1**: This is the core reliability improvement F-09 exists to deliver — transient failures currently vanish; this makes them self-heal.

**Topic chain**: `notification-requested` → `notification-requested-retry-5s` → `notification-requested-retry-1m` → `notification-requested-retry-10m` → `notification-requested-dlq`

**Required headers on every retry/DLQ message** (sourced from ROADMAP.md, captured from a personal Kafka/.NET study doc reviewed 2026-07-13):

| Header | Purpose |
| --- | --- |
| `x-original-topic` | Where the message originally came from |
| `x-retry-count` | Attempts so far |
| `x-first-failure-timestamp` | When it first failed |
| `x-exception-type` / `x-exception-message` | For triage without reproducing the failure |
| `x-next-retry-at` | Computed timestamp the retry-topic consumer checks before processing |

**Acceptance Criteria**:

1. WHEN the original consumer classifies a failure as Transient THEN it SHALL publish the message to `notification-requested-retry-5s` with `x-retry-count=1`, `x-next-retry-at` = now + 5s, and the other required headers, then commit its own offset (never blocks its partition)
2. WHEN a retry-stage consumer receives a message before its `x-next-retry-at` has elapsed THEN it SHALL NOT process it yet (exact mechanism — re-poll vs. pause/resume — is a Design decision, not fixed here)
3. WHEN a retry-stage consumer processes a message after `x-next-retry-at` and the retry succeeds THEN the notification SHALL reach its normal terminal status (`Sent`) exactly as if it had succeeded on the first attempt
4. WHEN a retry-stage consumer processes a message after `x-next-retry-at` and it fails again with a Transient classification THEN it SHALL publish to the next stage in the chain with `x-retry-count` incremented and a new `x-next-retry-at`
5. WHEN a retry attempt fails with a Poison Pill classification (discovered only during a retry attempt, not the first attempt) THEN it SHALL skip the remaining retry stages and publish directly to `notification-requested-dlq`
6. WHEN the `notification-requested-retry-10m` stage's retry attempt (the 3rd and final one) fails again with a Transient classification THEN the message SHALL be published to `notification-requested-dlq` instead of a further retry stage — retries are exhausted
7. WHEN the original consumer classifies a failure as Poison Pill THEN it SHALL publish directly to `notification-requested-dlq` (skipping the retry chain entirely) with `x-retry-count=0`

**Independent Test**: Publish a message that fails transiently on the first two attempts and succeeds on the third; assert it lands as `Sent` and passed through exactly the `-retry-5s` and `-retry-1m` topics (never reaching `-retry-10m` or the DLQ).

---

### P1: DLQ landing with full triage context ⭐ MVP

**User Story**: As an operator, I want every message that exhausts retry or is classified as poison pill to land in a DLQ topic with the original payload and failure context, so that I can triage failures without reproducing them from logs.

**Why P1**: Without this, F-09's retry chain would just delay the same "silently vanishes" problem E-03 has today by a few minutes.

**Acceptance Criteria**:

1. WHEN a message lands in `notification-requested-dlq` THEN it SHALL retain the original message payload unchanged, plus all headers from the classification table
2. WHEN a message lands in the DLQ THEN the notification's persisted record SHALL be marked `Failed` (existing `NotificationStatus`) via `UpdateStatusAsync`, with `FailureReason` set from `x-exception-message`

**Independent Test**: Force a poison-pill failure and assert the DLQ topic receives the message with all six required headers populated and the notification record shows `Failed`.

---

### P2: Reconciliation for crash-stuck notifications

**User Story**: As the system, I want a background job to find notifications stuck in `Dispatching` for more than 2 minutes and give them another real chance at delivery, so that a crash between "marked Dispatching" and "SES call completed" doesn't silently strand a notification forever.

**Why P2**: Important for correctness but lower frequency than the retry/DLQ path (only triggers on an actual process crash mid-dispatch, not on every transient failure) — the core retry/DLQ mechanism (P1) must exist first since reconciliation reuses it.

**Decision (confirmed with the user 2026-07-14):** a stuck notification is republished to the start of the retry chain (`notification-requested-retry-5s`), not marked `Failed` directly — it's treated as a transient failure that crashed before completing, and deserves the same real retry chance as any other transient failure.

**Acceptance Criteria**:

1. WHEN a notification has `Status = Dispatching` and has been in that state for more than 2 minutes THEN the reconciliation job SHALL republish it to `notification-requested-retry-5s` with `x-retry-count=1` and the appropriate headers
2. WHEN the reconciliation job runs THEN it SHALL query `GetByRecipientAsync`/an equivalent scan mechanism (exact query approach — GSI scan vs. new index — is a Design decision) rather than requiring a new DynamoDB table
3. WHEN the reconciliation job republishes a stuck notification THEN it SHALL run on a fixed interval (not on every dispatch) — exact interval is a Design/Tasks decision, informed by the 2-minute staleness threshold

**Independent Test**: Seed a notification record with `Status = Dispatching` and a `CreatedAt`/`UpdatedAt` more than 2 minutes in the past; run the reconciliation job once and assert it publishes to the retry-5s topic.

---

### P2: OTEL metrics for consumer lag and dispatch duration

**User Story**: As an operator, I want `kafka_consumer_lag_notification_requested` and a `notification_dispatch_duration_seconds` histogram exported via OTEL, so that I can see backlog growth and latency degradation before they become incidents.

**Why P2**: Observability, not a correctness requirement — F-09 functions without it, but per ROADMAP this belongs to F-09's scope (E-06 F-12 builds the actual SLO dashboards/alerts on top of these metrics).

**Acceptance Criteria**:

1. WHEN the consumer processes messages THEN `kafka_consumer_lag_notification_requested` SHALL reflect the current lag on the `notification-requested` consumer group
2. WHEN a dispatch attempt completes (success or failure) THEN `notification_dispatch_duration_seconds` SHALL record the elapsed time from message receipt to outcome

**Independent Test**: Process a message and assert both metrics are recorded via the existing OTEL test/export mechanism (exact assertion approach — Design decision, consistent with how E-01's OTEL wiring is already tested, if at all).

---

## Edge Cases

- WHEN the same message is processed twice due to a Kafka consumer-group rebalance re-delivery (already possible today) THEN `SaveIfNotExistsAsync`'s existing atomic idempotency (AD-008) SHALL still prevent a duplicate `Sent` — F-09's retry/DLQ routing operates entirely on top of that existing guarantee, not a replacement for it
- WHEN a retry-stage consumer crashes between publishing to the next stage and committing its own offset THEN the message may be redelivered and republished twice — acceptable per AD-008's existing idempotency (the eventual dispatch attempt is still deduplicated by `correlationId`), but the retry-topic message itself may appear more than once in a downstream topic; not treated as a correctness bug for this feature
- WHEN a message has already exhausted `x-retry-count` (e.g. a bug republishes an already-DLQ'd message) THEN the DLQ consumer/observability job SHALL NOT attempt to reprocess it — the DLQ is a terminal sink for this feature, not another retry stage
- WHEN the reconciliation job's republish (P2) itself fails to publish (Kafka producer error) THEN it SHALL log the failure and leave the record's `Dispatching` status untouched for the next reconciliation run to retry — reconciliation must be safe to re-run on the same stuck record without side effects beyond another publish attempt

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| REL-01 | P1: Failure classification | Implementing | Verified |
| REL-02 | P1: Failure classification | Implementing | Verified |
| REL-03 | P1: Failure classification | Implementing | Verified |
| REL-04 | P1: Retry topic chain | Implementing | Verified |
| REL-05 | P1: Retry topic chain | Implementing | Verified |
| REL-06 | P1: Retry topic chain | Implementing | Verified |
| REL-07 | P1: Retry topic chain | Implementing | Verified |
| REL-08 | P1: Retry topic chain | Implementing | Verified |
| REL-09 | P1: Retry topic chain | Implementing | Verified |
| REL-10 | P1: DLQ landing | Implementing | Verified |
| REL-11 | P1: DLQ landing | Implementing | Verified |
| REL-12 | P2: Reconciliation | Implementing | Verified |
| REL-13 | P2: Reconciliation | Implementing | Verified |
| REL-14 | P2: Reconciliation | Implementing | Verified |
| REL-15 | P2: OTEL metrics | Implementing | Verified |
| REL-16 | P2: OTEL metrics | Implementing | Verified |

**ID format:** `REL-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 16 total, 16 to be mapped in Design/Tasks, 0 unmapped

---

## Success Criteria

- [ ] Every dispatch failure classification matches the table above, verified per-row in tests
- [ ] A transient failure retried 3 times with the correct 5s/1m/10m backoff before landing in the DLQ
- [ ] A poison-pill failure lands directly in the DLQ, skipping the retry chain entirely
- [ ] The original consumer's offset is always committed — no partition is ever blocked, verified under a forced failure
- [ ] A notification stuck in `Dispatching` for >2 minutes is republished to the retry chain by the reconciliation job within one polling interval
- [ ] `kafka_consumer_lag_notification_requested` and `notification_dispatch_duration_seconds` are visible in the Aspire Dashboard locally
