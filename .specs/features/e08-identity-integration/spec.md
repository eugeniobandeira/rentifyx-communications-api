# E-08 · Identity-API Integration Contract — Specification

## Problem Statement

`rentifyx-identity-api` currently sends its own transactional email (registration confirmation, email verification, password reset) directly via AWS SES v2, duplicating the sender/template/consent logic this service now centralizes. The decision made now is: **design the integration contract in this cycle, migrate identity-api's code after communications-api's v1 (E-01–E-06) is stable in production.** This feature exists to lock the contract early — event schema, template ownership, auth-critical email guarantees — so identity-api's eventual migration is a swap of the sending mechanism, not a redesign.

## Goals

- [ ] `NotificationRequested` event contract explicitly covers identity-api's auth-critical email use cases (verification, password reset) without schema changes later
- [ ] Auth-critical email failure modes (e.g., password reset must not silently vanish into a DLQ with no visibility) are addressed by contract, not left implicit
- [ ] identity-api's own SES sender code is documented as the migration target, with a clear decommission trigger

## Out of Scope

| Feature | Reason |
| --- | --- |
| Actually removing identity-api's `SesEmailSender` | Deferred to post-E-06 migration, tracked as a Deferred Idea, not built now |
| New identity-api code changes of any kind | This feature only produces a contract/ADR artifact; no code changes in rentifyx-identity-api in this cycle |
| Marketing email from identity-api | Identity-api has no marketing use case today; out of scope until one exists |

---

## User Stories

### P1: IDINT-01 — Auth-Critical Event Contract ⭐ MVP

**User Story**: As the identity-api team (same engineer, future self), I want the `NotificationRequested` contract to explicitly support verification and password-reset email today, so the eventual migration doesn't require a breaking schema change.

**Why P1**: If the contract is designed only around asset-registry's use cases, identity-api's migration later forces a breaking change against a contract other producers already depend on.

**Acceptance Criteria**:

1. WHEN the `NotificationRequested` schema is reviewed THEN it SHALL already accommodate `templateId` values `EmailVerification` and `PasswordReset` without structural changes (payload shape: `recipientName`, a single time-boxed `actionLink` or `actionToken` field, `expiresAt`)
2. WHEN a password-reset-class notification is defined THEN its `templateId` SHALL be flagged in documentation as **auth-critical** — a category with a stricter delivery guarantee than generic transactional mail
3. WHEN an auth-critical notification fails after retries THEN it SHALL route to the DLQ same as any other failure (US-C021), but the DLQ record SHALL be tagged `severity=auth-critical` so operational tooling can page rather than passively queue for manual review
4. WHEN the contract doc is published THEN it SHALL live at `docs/contracts/notification-requested.md` (or equivalent AsyncAPI file per US-C028) and SHALL be the reference both services point to — not duplicated per-repo

**Independent Test**: Draft two example `NotificationRequested` payloads (`EmailVerification`, `PasswordReset`) against the actual schema and confirm both validate under the existing `ScribanTemplateRenderer` payload-validation rules (US-C014) without modification.

---

### P2: IDINT-02 — Migration Decommission Plan

**User Story**: As a tech lead, I want a documented trigger condition for when identity-api migrates off its own SES sender, so this doesn't become a "someday" item that never happens.

**Why P2**: Not blocking — the contract (IDINT-01) is what unblocks a future migration PR. This story just makes sure the migration itself doesn't get lost.

**Acceptance Criteria**:

1. WHEN communications-api reaches v1.0.0 and has run in production for an agreed stabilization window THEN a migration ADR SHALL be written in rentifyx-identity-api's repo describing the cutover (publish `NotificationRequested` instead of calling SES directly)
2. WHEN the migration ADR is written THEN it SHALL define a rollback path (feature flag or dual-write period) so auth email delivery is never at risk during cutover
3. WHEN the trigger condition is met THEN this item SHALL move from `Future Considerations` to an active roadmap epic in identity-api's own `.specs/`

**Independent Test**: N/A for this cycle — this story's "test" is procedural (the trigger condition and rollback plan exist in writing, reviewed).

---

## Edge Cases

- WHEN identity-api eventually migrates and both the old direct-SES path and the new event path exist during a transition THEN duplicate-send risk SHALL be avoided by feature-flagging one path off entirely per environment (no dual-active sending)
- WHEN the shared AWS SES account quota (already flagged as Watch Item / Blocker B-001 in communications-api's STATE.md) is sized THEN identity-api's eventual auth-critical volume SHALL be included in that sizing, not sized independently later

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| IDINT-01 | P1: Auth-Critical Event Contract | Design | Pending |
| IDINT-02 | P2: Migration Decommission Plan | - | Pending |

**Coverage:** 2 total, 0 mapped to tasks, 2 unmapped ⚠️ (this feature ships a contract/ADR artifact, not code — Tasks phase will be lightweight)

---

## Success Criteria

- [ ] `docs/contracts/notification-requested.md` exists and both `EmailVerification`/`PasswordReset` payloads validate against it
- [ ] identity-api's migration trigger condition and rollback plan are written down somewhere durable (this spec + a cross-reference note in identity-api's own memory, added manually since this session only has write access to communications-api)
- [ ] No code changes shipped to rentifyx-identity-api in this cycle — confirmed by scope boundary above
