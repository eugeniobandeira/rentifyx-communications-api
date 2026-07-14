# E-04 · F-07 — SES & DynamoDB Integration Specification

## Problem Statement

E-02/E-03 built a fully orchestrated dispatch pipeline (`DispatchNotificationHandler`) against four Domain contracts (`INotificationRepository`, `IConsentRepository`, `ITemplateRenderer`, `IEmailSender`), but none of them have a real implementation yet — the application cannot actually persist a notification, resolve real consent, render a real template, or send a real email. F-07 is the first of three E-04 sub-features and delivers the real adapters: DynamoDB for persistence, Scriban for rendering, SES for sending. Without this, nothing built in E-02/E-03 can run end-to-end.

## Goals

- [ ] `DynamoDbNotificationRepository` implements `INotificationRepository` against a single-table design, with `SaveIfNotExistsAsync` using a real atomic conditional write (AD-008) — no read-then-write race
- [ ] `DynamoDbConsentRepository` implements `IConsentRepository`
- [ ] `ScribanTemplateRenderer` implements `ITemplateRenderer` using Scriban (ADR-C05, confirmed 2026-07-13)
- [ ] `SesEmailSender` implements `IEmailSender` against real AWS SES; a `MockEmailSender` exists for local/test use where sending a real email is undesirable
- [ ] `Channel` and `NotificationStatus` are persisted as their **string name**, never the underlying `int` (confirmed 2026-07-13 — see STATE.md Todos)
- [ ] Integration tests for all four adapters run against **LocalStack via Testcontainers** — per AD-013 (2026-07-12), which superseded the real-dev-account plan for *automated* tests specifically; manual, occasional real-AWS runs remain a separate deliberate action, not part of this test suite
- [ ] All four adapters registered in `InfrastructureDependencyInjection`, replacing nothing (no existing registrations for these interfaces exist yet)

## Out of Scope

| Feature | Reason |
| --- | --- |
| Token-bucket rate limiter, Polly circuit breaker in front of `IEmailSender` | F-08 (next E-04 sub-feature) |
| Retry-topic chain, DLQ, poison-pill routing, reconciliation `IHostedService`, OTEL metrics | F-09 (final E-04 sub-feature) |
| Confirming the actual SES account sending-rate quota | Blocked on manual `aws ses get-send-quota` / AWS console lookup (B-001) — F-08's rate limiter uses a conservative placeholder (14/sec) until confirmed; not this feature's concern since F-07 doesn't rate-limit anything |
| Actual template *content* for every notification type the platform will eventually need | Only enough templates to exercise the pipeline end-to-end (e.g. one transactional example) — content authoring for identity-api's real templates (E-08) is separate |
| Marketing-specific consent semantics (opt-out, default-deny) | E-07 |
| DynamoDB table/GSI provisioning via Terraform | E-06 — this feature assumes the table already exists (manually provisioned per E-01 F-01, or created by the LocalStack Testcontainers fixture for tests) |

---

## User Stories

### P1: SESDB-US-01 — DynamoDB Single-Table Notification Persistence ⭐ MVP

**User Story**: As the platform, I want `DynamoDbNotificationRepository` to persist notifications atomically and support the three access patterns the domain needs (by id, by recipient, idempotent save), so `DispatchNotificationHandler`'s calls to `INotificationRepository` actually do something.

**Why P1**: Nothing else in F-07 matters if notifications can't be saved — this is the foundation every other story builds on.

**Acceptance Criteria**:

1. WHEN `SaveIfNotExistsAsync` is called with a new `NotificationEntity` THEN it SHALL write an item with `PK=NOTIF#{correlationId}` (corrected 2026-07-13 — see design.md Tech Decisions for why the key must be `CorrelationId`, not `Id`), `GSI1PK=RECIPIENT#{recipientId}`, `GSI2PK=ID#{id}` and return `true`
2. WHEN `SaveIfNotExistsAsync` is called twice with the same `CorrelationId` (simulating a Kafka re-delivery) THEN the second call SHALL use a `ConditionExpression: attribute_not_exists(PK)` on the primary key (keyed by `CorrelationId`), fail the write, and return `false` — NOT throw, NOT read-then-check (AD-008: no race window)
3. WHEN `GetByIdAsync` is called with an existing id THEN it SHALL query `GSI2` (not scan the table) and return the fully-hydrated `NotificationEntity` (including `Channel`, `NotificationStatus` as their string names, not numbers)
4. WHEN `GetByRecipientAsync` is called THEN it SHALL query `GSI1` (not scan the table) and return all notifications for that recipient
5. WHEN `UpdateStatusAsync` is called THEN it SHALL update only the status attribute (and `UpdatedAt`), not overwrite the whole item
6. WHEN a notification item is written THEN it SHALL include a `TTL` attribute set to `CreatedAt + 90 days` (LGPD Art. 46 data minimization)
7. WHEN `Channel` or `NotificationStatus` is serialized to DynamoDB THEN it SHALL be stored as its string name (`"Email"`, `"Sent"`), never as the underlying `int`

**Independent Test**: Integration test against a LocalStack DynamoDB container: create table, save a notification, verify raw item attributes via `IAmazonDynamoDB.GetItemAsync` show string enum values (not numbers); attempt a duplicate `SaveIfNotExistsAsync` and assert `false` with no exception; query by recipient and assert GSI1 was used (via LocalStack's item count / no full-table-scan assertion, or by asserting query latency/behavior is consistent with an indexed query in the test setup).

---

### P1: SESDB-US-02 — DynamoDB Consent Persistence ⭐ MVP

**User Story**: As the platform, I want `DynamoDbConsentRepository` to resolve a real consent record for (recipient, channel), so `DispatchNotificationHandler`'s consent-gated dispatch (E-02/E-03) is backed by real data instead of always getting `null`.

**Why P1**: Required for the dispatch handler's consent step to mean anything against a real backend.

**Acceptance Criteria**:

1. WHEN `FindAsync(recipientId, channel)` is called AND no record exists THEN it SHALL return `null` (not throw, not a default `ConsentPreference`)
2. WHEN a `ConsentPreference` record exists for that (recipient, channel) pair THEN `FindAsync` SHALL return it with `OptedIn` and `UpdatedAt` correctly hydrated
3. WHEN `Channel` is persisted as part of the consent record's key or attributes THEN it SHALL be stored as its string name, same rule as SESDB-US-01 AC7

**Independent Test**: Integration test against LocalStack DynamoDB: query with no record → `null`; seed a record with `OptedIn=false` → `FindAsync` returns it with the correct value.

---

### P1: SESDB-US-03 — Scriban Template Rendering ⭐ MVP

**User Story**: As the platform, I want `ScribanTemplateRenderer` to render a named template against a payload dictionary, validating that required fields are present, so `DispatchNotificationHandler`'s render step produces real email content instead of calling a contract with no implementation.

**Why P1**: Resolves ADR-C05 (confirmed 2026-07-13: Scriban) and is required for the send step to have real content.

**Acceptance Criteria**:

1. WHEN `RenderAsync(templateId, payload)` is called with a known `templateId` and a `payload` containing every field the template references THEN it SHALL return the rendered string with all placeholders substituted
2. WHEN `templateId` does not correspond to any known template THEN it SHALL return an `ErrorOr` error (not throw) — this is what triggers `DispatchNotificationHandler`'s `MarkFailed` path (E-03 DISPATCH-06)
3. WHEN `payload` is missing a field the template references THEN it SHALL return an `ErrorOr` validation error rather than rendering with a blank/null substitution
4. WHEN a template is rendered THEN Scriban SHALL run in its default restricted mode (no arbitrary file/network access from within a template) — templates are trusted content authored by the platform team, but the engine itself should not expose more capability than needed

**Independent Test**: Unit test (no I/O — templates loaded as embedded resources, see Design) rendering a known template with a complete payload → exact expected output string; missing field → validation error; unknown `templateId` → not-found error.

---

### P1: SESDB-US-04 — SES Email Sending ⭐ MVP

**User Story**: As the platform, I want `SesEmailSender` to send a rendered email via AWS SES, and a `MockEmailSender` available for contexts where sending a real email is undesirable, so `DispatchNotificationHandler`'s send step actually delivers mail in production while remaining safe to exercise locally/in CI.

**Why P1**: The final link in the outbox chain — without it, nothing actually reaches a recipient.

**Acceptance Criteria**:

1. WHEN `SendAsync(recipient, renderedContent)` is called THEN `SesEmailSender` SHALL call SES `SendEmailAsync` (or `SendRawEmailAsync`, per Design) with the recipient's address and the rendered content as the body
2. WHEN SES returns a throttling or send-quota error THEN `SendAsync` SHALL surface it as an `ErrorOr` error (not throw) — this is what triggers `DispatchNotificationHandler`'s `MarkFailed` path
3. WHEN SES send succeeds THEN `SendAsync` SHALL return `Result.Success`
4. WHEN `MockEmailSender` is used (local dev / test contexts, not production) THEN `SendAsync` SHALL record the call (for test assertions) and return success without any network call

**Independent Test**: Integration test against LocalStack SES: send an email, assert LocalStack recorded it (via its message-log API or SDK list-call, per LocalStack SES support); unit test for `MockEmailSender` asserting it records calls without I/O.

---

### P2: SESDB-US-05 — Adapter Registration & Config

**User Story**: As a developer, I want all four adapters registered in `InfrastructureDependencyInjection` following the existing `AWSOptions`/`AddAWSService<T>` pattern (from `SecretsManagerProvider`, E-01), so the app boots with real implementations wired in without any new configuration mechanism.

**Why P2**: Necessary for the app to actually run, but is pure wiring — no new behavior beyond what US-01 through US-04 already define.

**Acceptance Criteria**:

1. WHEN the application starts THEN `IAmazonDynamoDB` and `IAmazonSimpleEmailServiceV2` SHALL be registered via `services.AddAWSService<T>()`, using the same `AWS:Profile` fail-fast credential check already in place (E-01 T07) — no new credential mechanism
2. WHEN the application starts in a non-test environment THEN `SesEmailSender` (not `MockEmailSender`) SHALL be the registered `IEmailSender`
3. WHEN integration tests run THEN the Testcontainers LocalStack fixture SHALL override the AWS client's `ServiceURL`, matching the pattern already used for `SecretsManagerProvider`'s integration tests (E-01 T12/AD-013)

**Independent Test**: `dotnet run --project AppHost` boots without a DI resolution error; integration test fixture confirms LocalStack endpoint override is in effect (requests go to the LocalStack container, not real AWS).

---

## Edge Cases

- WHEN two `SaveIfNotExistsAsync` calls race for the same `CorrelationId` (simulating a Kafka consumer-group rebalance re-delivering the same message to two consumer instances) THEN exactly one SHALL succeed and the other SHALL return `false` — verified with a concurrent-call integration test, not just a sequential one
- WHEN a `NotificationEntity`'s `Payload` dictionary contains a value that isn't a valid DynamoDB string-set-safe value (e.g. contains characters requiring escaping) THEN it SHALL still round-trip correctly (DynamoDB string attributes handle arbitrary UTF-8; no custom escaping needed, but this should be asserted, not assumed)
- WHEN Scriban template source contains a syntax error (author mistake, not user input) THEN `RenderAsync` SHALL return an `ErrorOr` error at render time — there is no build-time template validation in this feature (that would be a nice-to-have for a later epic, not blocking here)
- WHEN LocalStack's SES emulation doesn't support a specific SES API surface (e.g. bounce/complaint SNS feedback, which is E-07 F-14's concern) THEN F-07's tests only exercise `SendEmailAsync`/`SendRawEmailAsync` — nothing else is asserted here

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| SESDB-01 | P1: SESDB-US-01 | Verified | Implemented — T03, T04 |
| SESDB-02 | P1: SESDB-US-01 | Verified | Implemented — T03 |
| SESDB-03 | P1: SESDB-US-02 | Verified | Implemented — T02, T05 |
| SESDB-04 | P1: SESDB-US-03 | Verified | Implemented — T06 |
| SESDB-05 | P1: SESDB-US-03 | Verified | Implemented — T06 |
| SESDB-06 | P1: SESDB-US-04 | Verified | Implemented — T02, T07 |
| SESDB-07 | P1: SESDB-US-04 | Verified | Implemented — T07, T08 |
| SESDB-08 | P2: SESDB-US-05 | Verified | Implemented — T09 |

**ID format:** `SESDB-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 8 total, 8 mapped to tasks, 0 unmapped ✅ — all implemented 2026-07-13, 25 new tests passing (9 LocalStack integration + 16 unit)

---

## Success Criteria

- [ ] A `NotificationEntity` saved via `DynamoDbNotificationRepository` round-trips through `GetByIdAsync` with `Channel`/`NotificationStatus` readable as their string names in the raw DynamoDB item
- [ ] A duplicate `SaveIfNotExistsAsync` call never succeeds twice for the same `CorrelationId`, verified under concurrent access
- [ ] A real (LocalStack) SES send succeeds for a valid recipient and rendered content
- [ ] `dotnet run --project AppHost` boots cleanly with all four adapters registered, no DI resolution errors
- [ ] Integration test suite (LocalStack Testcontainers) covers all four adapters and passes in CI without real AWS credentials
