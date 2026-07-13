# E-02 · Domain Model — Notification & Consent Specification

## Problem Statement

E-01 delivered the scaffold, CI, and infra plumbing, but the Domain layer is still empty template boilerplate. Before the application layer (E-03) can consume Kafka events and dispatch notifications, the domain needs to express the actual business rules — the notification lifecycle, channel-agnostic contracts, and consent enforcement — as a pure, framework-free, 100% unit-tested model. Getting this wrong (or coupling it to AWS/EF/Kafka types) means every later epic inherits the mistake.

## Goals

- [ ] `Notification` aggregate enforces the full status lifecycle (Pending → Rendering → Dispatching → Sent | Failed | Suppressed) with illegal transitions rejected
- [ ] Consent is checked inside the domain (`Notification.Dispatch()`), never trusted from a producer claim (AD-004 / ADR-C04)
- [ ] Channel-agnostic contracts (`Channel` enum with `Sms`/`Push` reserved) so adding a channel later is additive, not breaking (AD-002 / ADR-C02)
- [ ] Domain layer has zero references to AWS SDK, Kafka, EF Core, or any other framework/infra type
- [ ] 100% unit test coverage on Domain project (measurable via the existing CI coverage gate)

## Out of Scope

| Feature | Reason |
| --- | --- |
| `SesEmailSender`, `DynamoDbNotificationRepository` implementations | E-04 — domain only defines the contracts (`IEmailSender`, `INotificationRepository`) |
| Kafka message deserialization, `DispatchNotificationHandler` orchestration | E-03 — domain only exposes the aggregate methods the handler will call |
| `ScribanTemplateRenderer` implementation | E-04 — domain only defines `ITemplateRenderer` |
| Actual domain event dispatching/publishing (MediatR wiring, outbox table) | E-03 — domain only raises/records events on the aggregate; publishing is an application/infra concern |
| Marketing channel consent semantics (opt-out default-deny) | E-07 — this epic's consent default applies to transactional channels (Email now, Sms/Push later); Marketing's different default is designed and implemented in E-07 |
| HTTP endpoints for querying/updating consent | E-05 |

---

## User Stories

### P1: NOTIF-US-01 — Notification Aggregate & Status Lifecycle ⭐ MVP

**User Story**: As the application layer, I want a `Notification` aggregate root that enforces its own valid status transitions so that no code path can put a notification into an inconsistent state (e.g., `Sent` without ever having been `Dispatching`).

**Why P1**: Every later epic (E-03 handler, E-04 persistence, E-06 reconciliation) depends on the aggregate being the single source of truth for lifecycle rules — this can't be bolted on later without risking silent state corruption.

**Acceptance Criteria**:

1. WHEN a `Notification` is created THEN it SHALL start in `Pending` status with `Id`, `CorrelationId`, `RecipientId`, `Recipient` (`EmailAddress`), `Channel`, `TemplateId`, `Payload`, and `CreatedAt` set
2. WHEN `Notification.Dispatch()` is called on a `Pending` notification THEN it SHALL transition through `Rendering` → `Dispatching` and raise `NotificationDispatched`, UNLESS the recipient is opted out (see NOTIF-US-02) or Payload is invalid for the template
3. WHEN `Notification.MarkSent()` is called on a `Dispatching` notification THEN status SHALL become `Sent` and it SHALL raise `NotificationDelivered`
4. WHEN `Notification.MarkFailed(reason)` is called on a `Dispatching` notification THEN status SHALL become `Failed`, the failure reason SHALL be recorded, and it SHALL raise `NotificationFailed`
5. WHEN any transition is attempted from a terminal status (`Sent`, `Failed`, `Suppressed`) THEN the aggregate SHALL reject it (return an error, not throw or silently no-op)
6. WHEN any transition is attempted out of order (e.g., `MarkSent()` called while still `Pending`) THEN the aggregate SHALL reject it with a descriptive error
7. WHEN the aggregate raises a domain event THEN the event SHALL be added to an internal collection retrievable by the caller (e.g., `Notification.DomainEvents`) — no direct publishing from inside the aggregate

**Independent Test**: Unit tests construct a `Notification`, drive it through the full happy path (Pending → Rendering → Dispatching → Sent) asserting status and raised events at each step, then assert that every illegal transition (e.g., `Pending` → `Sent` directly) returns an error instead of mutating state.

---

### P1: NOTIF-US-02 — Consent Enforcement on Dispatch ⭐ MVP

**User Story**: As the platform, I want consent checked inside `Notification.Dispatch()` so that no producer service can bypass LGPD Art. 8 opt-out preferences, intentionally or by bug.

**Why P1**: This is the core compliance guarantee of the entire service (AD-004 / ADR-C04) — it must be enforced at the point of dispatch, not as an optional check some caller might forget.

**Acceptance Criteria**:

1. WHEN `Notification.Dispatch()` is called AND no `ConsentPreference` record exists for (recipient, channel) AND the channel is a transactional channel (Email in v1) THEN the dispatch SHALL proceed — **absence of a record defaults to opted-in for transactional channels** (confirmed 2026-07-13: transactional email has its own legal basis under LGPD Art. 7 and must not silently fail for recipients identity-api hasn't yet synced consent for)
2. WHEN `Notification.Dispatch()` is called AND a `ConsentPreference` record exists for (recipient, channel) with `OptedIn = false` THEN the dispatch SHALL NOT proceed — status SHALL become `Suppressed`, and `NotificationSuppressed` SHALL be raised
3. WHEN `Notification.Dispatch()` is called AND a `ConsentPreference` record exists with `OptedIn = true` THEN the dispatch SHALL proceed normally
4. WHEN a notification is `Suppressed` THEN it SHALL be treated as a terminal, non-error outcome — no retry, no DLQ routing (this is a normal business rule outcome, not a failure — see ROADMAP E-04 F-09 failure classification)
5. WHEN `Notification.Dispatch()` needs a consent decision THEN the aggregate SHALL receive the resolved `ConsentPreference` (or its absence) as an input/collaborator — the aggregate itself SHALL NOT perform I/O (no direct repository calls from Domain)

**Independent Test**: Unit tests call `Dispatch()` with (a) no consent record → proceeds, (b) `OptedIn = false` → `Suppressed` + event raised, (c) `OptedIn = true` → proceeds normally. All three assert final status and raised event, not just a boolean return.

---

### P1: NOTIF-US-03 — Channel-Agnostic Value Objects & Contracts ⭐ MVP

**User Story**: As a developer adding a future channel (Sms, Push), I want the domain contracts already shaped to be channel-agnostic so adding a channel is additive, not a breaking schema/contract change.

**Why P1**: AD-002/ADR-C02 already committed to this direction in E-01 planning; the value objects and contracts are the load-bearing part that must exist before E-03/E-04 can be built against them.

**Acceptance Criteria**:

1. WHEN the `Channel` enum is defined THEN it SHALL include `Email` (implemented), `Sms` and `Push` (reserved, unimplemented) — matching AD-002
2. WHEN an `EmailAddress` value object is constructed with an invalid format THEN construction SHALL fail (return an error) rather than allow an invalid address into the domain
3. WHEN a `TemplateId` value object is constructed THEN it SHALL enforce non-empty, and SHALL NOT validate that the template actually exists (that's `ITemplateRenderer`'s concern in E-04)
4. WHEN `ConsentPreference` is defined THEN it SHALL hold `RecipientId`, `Channel`, `OptedIn`, `UpdatedAt` — no channel-specific fields
5. WHEN `INotificationRepository` is defined THEN it SHALL expose `SaveIfNotExists`, `GetById`, `GetByRecipient`, `UpdateStatus` as contracts only (no implementation in this epic — E-04)
6. WHEN `IConsentRepository` is defined THEN it SHALL expose a method to resolve the `ConsentPreference` for a (recipientId, channel) pair, returning an explicit "no record" case distinct from `OptedIn = false`
7. WHEN `ITemplateRenderer` and `IEmailSender` are defined THEN they SHALL be channel-agnostic interfaces with no AWS SES or Scriban-specific types leaking into their signatures

**Independent Test**: Domain project compiles with zero references to `Amazon.*`, `Confluent.Kafka`, `Microsoft.EntityFrameworkCore`, or `Scriban` namespaces (verified via a simple project-reference/using-directive check); unit tests cover VO validation failure paths.

---

## Edge Cases

- WHEN `Notification.Dispatch()` is called twice on the same aggregate instance (already `Dispatching`) THEN the second call SHALL be rejected, not silently re-processed (double-dispatch protection at the aggregate level — separate from the infra-level idempotency in E-04/AD-008)
- WHEN `Payload` is missing a field the template requires THEN `Dispatch()` SHALL reject with a validation error before ever reaching `Dispatching` status (domain does the shape check; actual template compilation is E-04)
- WHEN `Channel` is `Sms` or `Push` (reserved, unimplemented) THEN construction SHALL succeed (the enum value is valid) but `Dispatch()` SHALL reject with a clear "channel not implemented" error rather than attempting to send
- WHEN `ConsentPreference.UpdatedAt` is in the future (clock skew from a bad producer) THEN the domain SHALL still honor its `OptedIn` value — clock validation is not a domain concern here

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| NOTIF-01 | P1: NOTIF-US-01 | Verified | Implemented — T03, T05, T13, T15 |
| NOTIF-02 | P1: NOTIF-US-01 | Verified | Implemented — T04, T14 |
| NOTIF-03 | P1: NOTIF-US-01 | Verified | Implemented — T01 |
| NOTIF-04 | P1: NOTIF-US-02 | Verified | Implemented — T08, T09, T14 |
| NOTIF-05 | P1: NOTIF-US-02 | Verified | Implemented — T14 |
| NOTIF-06 | P1: NOTIF-US-03 | Verified | Implemented — T02, T05, T06, T07, T10, T11, T12, T16 |
| NOTIF-07 | P1: NOTIF-US-01 | Verified | Implemented — T04, covered by `DomainEvents` assertions |

**ID format:** `NOTIF-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 7 total, 7 mapped to tasks, 0 unmapped ✅ — all implemented 2026-07-13, 29 unit tests passing

---

## Success Criteria

- [ ] `Notification` aggregate rejects every illegal status transition in unit tests (no throw, no silent no-op — explicit error result, consistent with the codebase's `ErrorOr<T>` convention from E-01)
- [ ] Consent default behavior for transactional Email is opt-in absent a record, opt-out only on an explicit `OptedIn = false` record
- [ ] Domain project has zero framework/infra references (verified by project reference check)
- [ ] Domain project unit test coverage reported by CI's coverage gate (contributes meaningfully toward closing the ~5.6% repo-wide gap flagged in E-01)
