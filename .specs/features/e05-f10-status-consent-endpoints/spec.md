# E-05 F-10: Status & Consent Endpoints Specification

## Problem Statement

Producers and internal tooling have no way to query notification delivery status or manage recipient consent through communications-api — everything today is Kafka-in, nothing out. LGPD Art. 8 requires an auditable trail of consent decisions, and `IConsentRepository` is currently read-only (`FindAsync` only), so consent can only be seeded by data migration, never updated through the service that owns it. This feature adds a minimal, secured HTTP surface for both concerns.

## Goals

- [ ] Producers/internal services can query notification status by id or by recipient without touching DynamoDB directly
- [ ] Recipients' consent can be read and updated through the service that enforces it (closing the gap left by AD-004's read-only `IConsentRepository`)
- [ ] Every consent change is auditable (who/when/what changed) per LGPD Art. 8
- [ ] Endpoints are protected against unauthenticated access and enumeration abuse
- [ ] API contract is self-documenting (Scalar UI) and the async Kafka contract is documented for external consumers

## Out of Scope

| Feature | Reason |
|---|---|
| JWT/identity-api auth integration | E-08 migration not started yet; API-key auth is sufficient for service-to-service calls in this cycle (user decision, this session) |
| Consent read/write for Marketing channel semantics (opt-out default-deny) | E-07 scope — this feature only touches transactional consent (opt-in default, AD-013) |
| Admin UI for browsing audit history | No consumer for it yet; audit data is written and queryable via repository, not exposed via its own endpoint in P1 |
| Notification creation/dispatch via HTTP | AD-001 — intake stays Kafka-only, this feature is read/query + consent write only |
| Full AsyncAPI tooling/spec validation pipeline | P1 ships a hand-written markdown contract doc (`docs/contracts/notification-requested.md`, already referenced by STATE.md/AD-011); formal AsyncAPI spec generation is P3 |

---

## User Stories

### P1: Query notification status by id ⭐ MVP

**User Story**: As a producer service, I want to look up a notification's current status by its id so that I can confirm whether a dispatch succeeded without polling DynamoDB directly.

**Why P1**: Simplest read path; `INotificationRepository.GetByIdAsync` and the DynamoDB GSI2 lookup already exist (survey confirmed) — this is a thin HTTP wrapper, no new persistence work.

**Acceptance Criteria**:

1. WHEN a valid API key and existing notification id are provided THEN system SHALL return 200 with id, correlationId, recipientId, channel, status, failureReason (if any), createdAt, updatedAt
2. WHEN the notification id does not exist THEN system SHALL return 404
3. WHEN no API key or an invalid API key is provided THEN system SHALL return 401
4. WHEN the request exceeds rate limit THEN system SHALL return 429

**Independent Test**: Dispatch a notification via Kafka, then `GET /v1/api/notifications/{id}` with a valid key and confirm the returned status matches DynamoDB.

---

### P1: Query notifications by recipient ⭐ MVP

**User Story**: As a producer service, I want to list a recipient's recent notifications so that I can show delivery history without a direct DynamoDB dependency.

**Why P1**: Same rationale as above — `GetByRecipientAsync` + GSI1 already implemented.

**Acceptance Criteria**:

1. WHEN a valid API key and recipientId are provided THEN system SHALL return 200 with a list of notifications (id, channel, status, createdAt) for that recipient
2. WHEN the recipient has no notifications THEN system SHALL return 200 with an empty list (not 404)
3. WHEN no API key or an invalid API key is provided THEN system SHALL return 401

**Independent Test**: Dispatch two notifications to the same recipientId, `GET /v1/api/notifications/recipient/{recipientId}`, confirm both appear.

---

### P1: Read recipient consent ⭐ MVP

**User Story**: As a producer service, I want to check a recipient's current consent state before deciding whether to publish a `NotificationRequested` event, so I don't rely on stale local state.

**Why P1**: Read path already exists at the repository layer (`IConsentRepository.FindAsync`) — thin wrapper only.

**Acceptance Criteria**:

1. WHEN a valid API key and recipientId+channel are provided THEN system SHALL return 200 with `{ recipientId, channel, optedIn, updatedAt }`
2. WHEN no consent record exists for that recipient+channel THEN system SHALL return 200 with the default per AD-013 (`optedIn: true` for transactional channels, per "no record = not suppressed")
3. WHEN no API key or an invalid API key is provided THEN system SHALL return 401

**Independent Test**: Query consent for a recipient with no prior record, confirm `optedIn: true` is returned (matches `ConsentDecision.NoRecordFound()` domain default).

---

### P1: Update recipient consent with audit trail ⭐ MVP

**User Story**: As a producer service (e.g. an unsubscribe flow elsewhere), I want to update a recipient's consent so that future dispatches respect the new decision, with every change recorded for LGPD compliance.

**Why P1**: This is the actual gap AD-004/`IConsentRepository` left open — without it, consent can never change after initial seeding.

**Acceptance Criteria**:

1. WHEN a valid API key and a body `{ optedIn: bool }` are provided for a recipientId+channel THEN system SHALL call `IConsentRepository.UpdateAsync` to create-or-update the consent record and return 200 with the updated state
2. WHEN the update succeeds THEN system SHALL call `IConsentAuditRepository.AddAsync` to append an audit record (`AUDIT#{recipientId}#{timestamp}`) capturing recipientId, channel, previous optedIn value (or null if no prior record), new optedIn value, and timestamp — before returning the response
3. WHEN no API key or an invalid API key is provided THEN system SHALL return 401
4. WHEN the request body is missing `optedIn` or has an invalid type THEN system SHALL return 400 with a validation error

**Independent Test**: `PUT /v1/api/consent/{recipientId}` with `{ channel: "Email", optedIn: false }`, then confirm a subsequent `GET` reflects the change and an audit item exists in DynamoDB for that change.

---

### P2: Consent endpoint rate limiting (stricter than default)

**User Story**: As the service operator, I want consent endpoints rate-limited more tightly than general API traffic so that a leaked API key can't be used to enumerate or spam-toggle recipient consent at scale.

**Why P2**: Defense-in-depth on top of API-key auth (P1) — auth alone doesn't prevent a compromised key from being abused at volume.

**Acceptance Criteria**:

1. WHEN requests to `/v1/api/consent/*` exceed a dedicated, stricter rate-limit policy THEN system SHALL return 429, independent of the default `fixed` policy already applied to all `v1` routes
2. WHEN the limit is not exceeded THEN system SHALL process the request normally

**Independent Test**: Fire requests to `/v1/api/consent/{recipientId}` past the configured threshold, confirm 429 well before the default `fixed` policy's `PermitLimit` would trigger.

---

### P2: Security headers on all responses

**User Story**: As the service operator, I want standard security headers on every HTTP response so that the API meets baseline hardening expectations ahead of E-06's OWASP ZAP gate.

**Why P2**: Not blocking for the endpoints to function, but required before E-06's "OWASP ZAP clean" success criterion — cheaper to add now than retrofit.

**Acceptance Criteria**:

1. WHEN any endpoint (including `/health`) responds THEN system SHALL include `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Content-Security-Policy` headers
2. WHEN running in Development THEN system SHALL still include these headers (parity with prod, catch regressions early)

**Independent Test**: `curl -I` any endpoint, confirm all four headers present.

---

### P2: Scalar UI and Kafka contract doc published

**User Story**: As a new engineer or an external producer team, I want a browsable API doc and a written description of the `NotificationRequested` Kafka contract so that I can integrate without reading source code.

**Why P2**: Scalar/OpenAPI generation is already wired (survey confirmed `OpenApiExtensions.cs`, dev-only) — this story is about exposing the new endpoints there and finishing the previously-referenced contract doc, not building new tooling.

**Acceptance Criteria**:

1. WHEN `/scalar` is loaded in a non-Production environment THEN system SHALL list all four new endpoints with request/response schemas
2. WHEN `docs/contracts/notification-requested.md` is read THEN it SHALL document the `NotificationRequested` event schema, required fields, and the `channel` enum's reserved-but-unimplemented values (AD-002)

**Independent Test**: Load `/scalar` locally, confirm new endpoints appear; open the markdown doc and confirm it matches the current `DispatchNotificationRequest` shape.

---

### P3: Formal AsyncAPI spec generation

**User Story**: As a producer team's tooling, I want a machine-readable AsyncAPI spec for `NotificationRequested` so that client code/schema validation can be generated automatically.

**Why P3**: Nice-to-have; the hand-written markdown doc (P2) already unblocks human integration. No current consumer asked for machine-readable AsyncAPI.

**Acceptance Criteria**:

1. WHEN an AsyncAPI YAML/JSON spec is generated for `NotificationRequested` THEN it SHALL validate against the AsyncAPI 2.x/3.x schema

---

## Edge Cases

- WHEN a `GET /notifications/{id}` id is a syntactically invalid GUID THEN system SHALL return 400, not 404 or 500
- WHEN `PUT /consent/{recipientId}` targets a channel not yet implemented (e.g. `Sms`, `Push` per AD-002) THEN system SHALL still accept and persist it (channel-agnostic contract), but MAY document that only `Email` has enforcement behavior today
- WHEN two concurrent `PUT`s race for the same recipient+channel THEN `UpdateAsync`'s DynamoDB write SHALL apply last-write-wins (no optimistic locking required for P1 — flag as a known limitation, not a bug)
- WHEN the API key header is present but empty string THEN system SHALL return 401, not treat as anonymous/default
- WHEN `GetByRecipientAsync` returns a very large notification history THEN system SHALL apply a reasonable page size and note pagination as a P3/future concern if not implemented in P1 (avoid unbounded DynamoDB query cost)

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
|---|---|---|---|
| API-01 | P1: Query notification status by id | Design | Pending |
| API-02 | P1: Query notifications by recipient | Design | Pending |
| API-03 | P1: Read recipient consent | Design | Pending |
| API-04 | P1: Update recipient consent with audit trail | Design | Pending |
| API-05 | P1: API key authentication middleware | Design | Pending |
| API-06 | P2: Consent endpoint rate limiting | Design | Pending |
| API-07 | P2: Security headers middleware | Design | Pending |
| API-08 | P2: Scalar UI exposure + Kafka contract doc | Design | Pending |
| API-09 | P3: Formal AsyncAPI spec generation | - | Pending |

**Coverage:** 9 total, 0 mapped to tasks (pre-Design), 9 unmapped ⚠️ (expected at Specify stage)

---

## Decisions Captured This Session

- **Auth:** API-key header, service-to-service only (not JWT/OAuth). No identity-api coupling this cycle — consistent with AD-011 (contract-only integration, migration deferred).
- **Audit mechanism:** Append-only DynamoDB item per consent change (`AUDIT#{recipientId}#{timestamp}`), same single-table design as existing `NOTIF#`/`CONSENT#` items — no new audit service/store.
- **Method naming convention:** New/changed repository methods follow the verb set already established by `INotificationRepository` (`Get*Async`, `Save*Async`, `Update*Async`) — not the one-off `Find*Async` `IConsentRepository` currently uses. Design phase must:
  - Rename `IConsentRepository.FindAsync` → `GetAsync` (same signature, aligns with `GetByIdAsync`/`GetByRecipientAsync`) — update all call sites (`ConsentEnforcement`/`DispatchNotificationHandler` per AD-004).
  - Add `IConsentRepository.UpdateAsync(ConsentPreference consent, CancellationToken)` for the P1 consent write (covers create-or-update; consent always has a default per AD-013, so there's no true "insert vs. update" branch a caller needs to distinguish — `Update` alone matches the verb set, no `Upsert`).
  - Add a new `IConsentAuditRepository.AddAsync(ConsentAuditEntry entry, CancellationToken)` for the append-only audit write (`Add`, not `Save`/`Insert` — matches the CRUD verb set, and it's a pure insert with no existence check, unlike `SaveIfNotExistsAsync`'s idempotency semantics).
  - New HTTP-facing status query methods reuse `INotificationRepository.GetByIdAsync`/`GetByRecipientAsync` as-is — no renaming needed there, they already match the convention.

## Open Questions for Design Phase

- Exact API-key storage/rotation: Secrets Manager (matches existing `SecretsManagerProvider` pattern) vs. `IOptions<T>`-bound static config? Existing `SecretsProviderOptions` precedent suggests Secrets Manager.
- Whether audit-write (`IConsentAuditRepository.AddAsync`) happens inside the same handler call right after `IConsentRepository.UpdateAsync`, or is wrapped together behind a single application-layer method — Design must define the call sequence and failure handling if the audit write fails after the consent write succeeds.
- Does the audit item need its own GSI for "list all consent changes for recipient X" queries, or is point-in-time lookup via `AUDIT#{recipientId}#{timestamp}` sort-key range sufficient? (Likely sufficient — single-table sort-key range query — but confirm in Design.)

---

## Success Criteria

- [ ] All four P1 endpoints return correct data against a real notification/consent record in the AWS dev account
- [ ] `PUT /consent` produces a queryable audit trail — verified by reading back audit items after a test update
- [ ] Unauthenticated requests to any of the four endpoints receive 401, verified by an integration test
- [ ] `/scalar` lists all new endpoints with accurate schemas
