# E-03 · Application Layer — Use Cases Specification

## Problem Statement

E-02 delivered a pure `NotificationEntity` aggregate with lifecycle rules and consent enforcement, but nothing calls it yet — the Kafka consumer registered in E-01 is a bare skeleton (`// No processing logic yet - E-03 injects the dispatch pipeline here.`) that consumes and immediately commits every message without doing anything. E-03 wires the two together: deserialize `NotificationRequested` events, drive them through the aggregate's `Dispatch()`/`MarkSent()`/`MarkFailed()` sequence via the Domain contracts (`INotificationRepository`, `IConsentRepository`, `ITemplateRenderer`, `IEmailSender`), and persist the outcome — all orchestration logic, testable via mocks, with zero real AWS/Scriban implementation (that's E-04).

## Goals

- [ ] Every valid `NotificationRequested` Kafka message results in a `NotificationEntity` that is created, idempotently saved, dispatched (or suppressed), and reaches a terminal status (`Sent`, `Failed`, or `Suppressed`)
- [ ] Duplicate messages (same `correlationId`) are acknowledged without reprocessing — no double-send
- [ ] Consent is resolved once per message and fed into `NotificationEntity.Dispatch()` — the handler never bypasses the aggregate's consent gate
- [ ] Malformed or failing messages never crash the consumer loop or block subsequent messages
- [ ] `DispatchNotificationHandler` and its collaborators are covered by unit tests using mocked Domain contracts (no real Kafka/DynamoDB/SES needed to test this epic)

## Out of Scope

| Feature | Reason |
| --- | --- |
| `DynamoDbNotificationRepository`, `SesEmailSender`, `ScribanTemplateRenderer` real implementations | E-04 — this epic only calls the Domain interfaces; DI registration of concrete adapters happens in E-04 |
| Retry-topic chain, DLQ Kafka topic, poison-pill routing | E-04 F-09 — until then, failures are logged and the offset is committed (see NOTIF-DISPATCH-09 below) |
| Reconciliation `IHostedService` for notifications stuck in `Dispatching` | E-04 F-09 — E-03 only guarantees the status sequence (`Pending` → `Dispatching` before `Sent`) that makes future reconciliation possible |
| Token-bucket rate limiting, Polly circuit breaker in front of `IEmailSender` | E-04 F-08 |
| Consumer lag / dispatch duration OTEL metrics | E-04 F-09 |
| HTTP endpoints for querying notification/consent status | E-05 |

---

## User Stories

### P1: DISPATCH-US-01 — Deserialize and Validate `NotificationRequested` ⭐ MVP

**User Story**: As the platform, I want incoming Kafka messages validated against the `NotificationRequested` contract before touching the domain, so malformed producer messages never reach `NotificationEntity.Create()` with garbage data.

**Why P1**: Every other story depends on having a well-formed, validated message to act on.

**Acceptance Criteria**:

1. WHEN a Kafka message arrives on `notification-requested` THEN it SHALL be deserialized into a `NotificationRequestedMessage` contract (`correlationId`, `recipientId`, `recipientEmail`, `channel`, `templateId`, `payload: IDictionary<string,string>`)
2. WHEN deserialization fails (malformed JSON) THEN the consumer SHALL log the raw payload at `Error` level and commit the offset (per NOTIF-DISPATCH-09 — no crash, no infinite retry loop)
3. WHEN the deserialized message is missing a required field (e.g. empty `correlationId`, empty `recipientEmail`) THEN it SHALL fail validation before `NotificationEntity.Create()` is called, logged at `Error` level, offset committed
4. WHEN the message passes validation THEN `NotificationEntity.Create(...)` SHALL be called with the validated fields, converting `recipientEmail`/`templateId` through `EmailAddress.Create()`/`TemplateId.Create()` first

**Independent Test**: Unit test `DispatchNotificationHandler` (or its validator) with a valid message → proceeds to Create; with missing/malformed fields → returns a validation error without calling any repository/sender mock.

---

### P1: DISPATCH-US-02 — Idempotent Intake ⭐ MVP

**User Story**: As the platform, I want duplicate `NotificationRequested` messages (e.g. from a Kafka consumer-group rebalance re-delivery) acknowledged without reprocessing, so recipients never get double-sent an email.

**Why P1**: AD-008's atomic idempotency guarantee is only real if the application layer actually calls `SaveIfNotExistsAsync` before doing anything else — this is the integration point that makes the Domain contract meaningful.

**Acceptance Criteria**:

1. WHEN a validated `NotificationEntity` is created THEN `INotificationRepository.SaveIfNotExistsAsync` SHALL be called before any consent check, render, or send
2. WHEN `SaveIfNotExistsAsync` returns `false` (duplicate `correlationId`) THEN the handler SHALL treat this as success — log at `Information` level and commit the offset, with NO call to `Dispatch()`, `ITemplateRenderer`, or `IEmailSender`
3. WHEN `SaveIfNotExistsAsync` returns `true` (new record) THEN the handler SHALL proceed to consent resolution and dispatch

**Independent Test**: Unit test with a mocked `INotificationRepository` returning `false` → assert `IConsentRepository`/`ITemplateRenderer`/`IEmailSender` mocks were never invoked (`Mock.Verify(..., Times.Never)`).

---

### P1: DISPATCH-US-03 — Full Dispatch Orchestration ⭐ MVP

**User Story**: As the platform, I want the full outbox sequence (persist Pending → resolve consent → render → mark Dispatching → send → mark Sent/Failed) driven by `DispatchNotificationHandler`, so the aggregate's lifecycle rules from E-02 are actually exercised end-to-end.

**Why P1**: This is the MVP vertical slice — without it, E-02's aggregate is unused code.

**Acceptance Criteria**:

1. WHEN a new (non-duplicate) `NotificationEntity` is saved THEN `IConsentRepository.FindAsync(recipientId, channel)` SHALL be called, and its result (or absence) SHALL be converted to a `ConsentDecision` (`FromPreference` or `NoRecordFound`)
2. WHEN `NotificationEntity.Dispatch(consentDecision, isPayloadValid: true)` succeeds with a non-suppressed outcome THEN `ITemplateRenderer.RenderAsync(templateId, payload)` SHALL be called
3. WHEN rendering succeeds THEN `INotificationRepository.UpdateStatusAsync(id, Dispatching)` SHALL be called BEFORE `IEmailSender.SendAsync` is invoked (outbox ordering — AD-007: never call SES before the Dispatching status is durably persisted)
4. WHEN `IEmailSender.SendAsync` succeeds THEN `NotificationEntity.MarkSent()` SHALL be called and `INotificationRepository.UpdateStatusAsync(id, Sent)` SHALL persist the final state
5. WHEN `IEmailSender.SendAsync` fails (returns an `ErrorOr` error) THEN `NotificationEntity.MarkFailed(reason)` SHALL be called and `INotificationRepository.UpdateStatusAsync(id, Failed)` SHALL persist the final state — this is NOT re-thrown as an unhandled exception
6. WHEN `ITemplateRenderer.RenderAsync` fails (template not found, payload doesn't match template fields) THEN the handler SHALL call `NotificationEntity.MarkFailed(reason)` and persist `Failed` WITHOUT ever calling `IEmailSender` — the payload-shape gate belongs to rendering, not sending

**Independent Test**: Unit test the full happy path with mocked contracts, asserting the exact call order (`SaveIfNotExists` → `FindAsync` → `Dispatch` → `RenderAsync` → `UpdateStatusAsync(Dispatching)` → `SendAsync` → `MarkSent` → `UpdateStatusAsync(Sent)`) via Moq's `MockSequence` or call-order assertions.

---

### P1: DISPATCH-US-04 — Suppressed (Opted-Out) Path ⭐ MVP

**User Story**: As the platform, I want an opted-out recipient's notification to short-circuit entirely — no render, no send — so LGPD consent enforcement (AD-004) actually skips SES, not just skips at the aggregate's in-memory level.

**Why P1**: NOTIF-04/05 from E-02 defined this at the aggregate level; this story is what makes it observable end-to-end.

**Acceptance Criteria**:

1. WHEN `NotificationEntity.Dispatch(...)` returns a `Suppressed` outcome (consent record with `OptedIn = false`) THEN `ITemplateRenderer` and `IEmailSender` SHALL NOT be called
2. WHEN suppression occurs THEN `INotificationRepository.UpdateStatusAsync(id, Suppressed)` SHALL persist the final state
3. WHEN suppression occurs THEN the handler SHALL log at `Information` level (this is an expected business outcome, not a warning or error) and commit the Kafka offset normally

**Independent Test**: Unit test with a mocked `IConsentRepository` returning `OptedIn = false` → assert `ITemplateRenderer`/`IEmailSender` mocks never invoked, `UpdateStatusAsync` called with `Suppressed`.

---

### P1: DISPATCH-US-05 — Consumer Resilience on Processing Failure ⭐ MVP

**User Story**: As the platform operator, I want a single malformed or failing message to never stall the entire consumer, so one bad producer payload doesn't block every subsequent notification.

**Why P1**: Confirmed 2026-07-13 — until E-04 builds the real retry/DLQ topic chain, the interim behavior is: log everything needed for manual recovery, then move on. Blocking the partition on any failure is explicitly rejected as worse (a poison pill would stall all traffic indefinitely).

**Acceptance Criteria**:

1. WHEN message deserialization fails THEN the consumer SHALL log the raw message payload (or as much as can be salvaged) at `Error` level, including the Kafka partition/offset, and commit the offset
2. WHEN `DispatchNotificationHandler` throws an unexpected (non-`ErrorOr`) exception THEN the consumer's message-processing loop SHALL catch it, log at `Error` level with the `correlationId` if known, and commit the offset — the loop itself SHALL NOT terminate
3. WHEN any single message fails THEN subsequent messages on the topic SHALL continue to be processed normally (no consumer restart, no partition stall)

**Independent Test**: Unit/integration test feeding the consumer a malformed message followed by a valid one → assert the valid message is still processed and dispatched.

---

## Edge Cases

- WHEN `recipientEmail` is present but fails `EmailAddress.Create()` validation (e.g. `"not-an-email"`) THEN this is treated as a validation failure per DISPATCH-US-01, not a domain `NotificationEntity.Create()` call
- WHEN `channel` in the Kafka message is a string that doesn't map to any `Channel` enum value (unknown future channel) THEN the message SHALL be treated as a validation failure (same path as DISPATCH-US-01 AC3), not silently defaulted to `Email`
- WHEN `SaveIfNotExistsAsync` itself throws (e.g. a transient DynamoDB error, once E-04 lands) THEN this epic's handler propagates it as an unhandled exception, caught by the consumer's DISPATCH-US-05 safety net — real retry-vs-poison-pill classification is E-04's job, not this epic's
- WHEN a `NotificationEntity` is successfully created but the process crashes between `UpdateStatusAsync(Dispatching)` and `MarkSent()`/`MarkFailed()` THEN the record is left in `Dispatching` — E-03 does not resolve this (that's the E-04 reconciliation job); E-03's only obligation is that this durable intermediate state exists at all

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| DISPATCH-01 | P1: DISPATCH-US-01 | Design | Pending |
| DISPATCH-02 | P1: DISPATCH-US-01 | Design | Pending |
| DISPATCH-03 | P1: DISPATCH-US-02 | Design | Pending |
| DISPATCH-04 | P1: DISPATCH-US-02 | Design | Pending |
| DISPATCH-05 | P1: DISPATCH-US-03 | Design | Pending |
| DISPATCH-06 | P1: DISPATCH-US-03 | Design | Pending |
| DISPATCH-07 | P1: DISPATCH-US-04 | Design | Pending |
| DISPATCH-08 | P1: DISPATCH-US-04 | Design | Pending |
| DISPATCH-09 | P1: DISPATCH-US-05 | Design | Pending |

**ID format:** `DISPATCH-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 9 total, 0 mapped to tasks, 9 unmapped ⚠️ (Design phase next)

---

## Success Criteria

- [ ] A valid `NotificationRequested` message results in a `Sent` or `Failed` notification record, never left `Pending`
- [ ] Sending the same `correlationId` twice never calls `IEmailSender` twice
- [ ] An opted-out recipient never reaches `ITemplateRenderer` or `IEmailSender`
- [ ] A malformed message never stops the consumer from processing the next message
- [ ] `DispatchNotificationHandler` reaches meaningful unit test coverage using mocked Domain contracts (contributes toward closing the repo-wide 80% coverage gate — currently 17.4% after E-02)
