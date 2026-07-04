# State

**Last Updated:** 2026-07-03T00:00:00Z
**Current Work:** Project Initialization

---

## Recent Decisions (Last 60 days)

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
- [ ] SES bounce/complaint webhook processing (v1.1 backlog) — needed for long-term sender reputation health. Captured during: project initialization.
- [ ] Token-bucket resizing strategy when both identity-api and communications-api are under heavy concurrent load. Captured during: project initialization.

---

## Todos

- [ ] Confirm actual AWS SES sending-rate quota (account-level) before implementing US-C019 token bucket — resolves B-001.
- [ ] Decide on Scriban vs. Razor Class Library for template rendering (ADR-C05) — currently open choice, both are valid.
- [ ] Verify identity-api's Kafka broker config is reusable for local dev, or set up standalone Kafka container in AppHost (US-C002).

---

## Preferences

**Model Guidance Shown:** never
