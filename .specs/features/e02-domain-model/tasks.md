# E-02 · Domain Model — Notification & Consent Tasks

**Design**: `.specs/features/e02-domain-model/design.md`
**Status**: Done

**Execution note (2026-07-13):** All 16 tasks implemented directly (not via sub-agent delegation — code was small/mechanical enough that direct implementation was faster than spawning 16 sub-agents). One deviation from plan: T05 (`NotificationErrorCodes`) didn't anticipate `InvalidRecipientId`, needed by T08 (`ConsentPreference`) — added as a same-commit amendment, not a separate task. Final state: 29 unit tests passing (`dotnet test 03-tests/00-Domain/... --filter "Category!=Integration"`), repo-wide line coverage rose from ~5.6% to 17.4% (still below the 80% CI gate, but real progress — see STATE.md).

---

## Test Strategy (`.specs/codebase/TESTING.md` does not exist yet — reusing the gate commands already established in `.specs/features/e01-foundation/tasks.md`)

| Gate | Command | When |
| --- | --- | --- |
| **build** | `dotnet build --no-incremental` | Enums, records, constants, interfaces with no logic |
| **quick** | `dotnet test --filter "Category!=Integration"` | Unit tests only (Domain has zero integration surface — no I/O) |

## Test Coverage Matrix

| Code Layer | Test Type Required | Parallel-Safe |
| --- | --- | --- |
| Enums (`Channel`, `NotificationStatus`) | none (no logic) | Yes |
| Domain event records | none (no logic) | Yes |
| Error code constants | none (no logic) | Yes |
| Value Objects (`EmailAddress`, `TemplateId`, `ConsentPreference`, `ConsentDecision`) | unit | Yes — pure in-memory xUnit, no shared state |
| `Notification` aggregate | unit | Yes — same as above |
| Contract interfaces (no implementation) | none | Yes |

**Parallelism Assessment**: All Domain unit tests are pure, in-process xUnit facts/theories with no shared fixtures, no Testcontainers, no filesystem/network access — unlike E-01's integration tests, every `[P]` flag below is safe.

---

## Execution Plan

```
Phase 1 — Foundation (Parallel):
  T01 [P]  Tests.Domain project scaffold
  T02 [P]  Channel enum
  T03 [P]  NotificationStatus enum
  T04 [P]  Domain events (IDomainEvent + 4 records)
  T05 [P]  NotificationErrorCodes constants

Phase 2 — Value Objects (Parallel, after T01 + T02):
  T01, T02 ──┬──→ T06 [P]  EmailAddress VO
             ├──→ T07 [P]  TemplateId VO
             └──→ T08 [P]  ConsentPreference VO

Phase 3 — Consent Decision + Simple Contracts (Parallel, after Phase 2):
  T08 ──→ T09 [P]  ConsentDecision
  T08, T02 ──→ T10 [P]  IConsentRepository interface
  T07 ──→ T11 [P]  ITemplateRenderer interface
  T06 ──→ T12 [P]  IEmailSender interface

Phase 4 — Aggregate (Sequential — same file evolves across 3 tasks):
  T02, T03, T05, T06, T07 ──→ T13  Notification.Create()
  T13, T04, T09 ──→ T14  Notification.Dispatch()
  T14 ──→ T15  Notification.MarkSent() / MarkFailed()

Phase 5 — Repository Contract (Sequential, after Aggregate exists):
  T15, T03 ──→ T16  INotificationRepository interface
```

---

## Task Breakdown

### T01: Create `RentifyxCommunications.Tests.Domain` project scaffold [P]

**What**: New xUnit test project targeting the Domain layer in isolation (none of the existing 6 test projects fit — see design.md Code Reuse Analysis)
**Where**: `03-tests/00-Domain/RentifyxCommunications.Tests.Domain/RentifyxCommunications.Tests.Domain.csproj`, added to `RentifyxCommunications.slnx`
**Depends on**: None
**Reuses**: Package set from `03-tests/03-Handlers/RentifyxCommunications.Tests.Handlers.csproj` (xunit, xunit.runner.visualstudio, FluentAssertions, Microsoft.NET.Test.Sdk, coverlet.collector) — omit `Moq` (Domain has nothing to mock, zero I/O dependencies)
**Requirement**: NOTIF-03 (supports all Domain testing)

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Project created with `ProjectReference` to `RentifyxCommunications.Domain.csproj` only
- [ ] Project added to `RentifyxCommunications.slnx` under a new `/03-tests/00-Domain/` folder
- [ ] `dotnet build --no-incremental` passes with zero errors

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`; `dotnet test --list-tests --project 03-tests/00-Domain/...` returns empty list (no tests yet, project just compiles)

**Commit**: `chore(domain): scaffold Tests.Domain project`

---

### T02: `Channel` enum [P]

**What**: `Email`, `Sms` (reserved), `Push` (reserved) per AD-002
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Enums/Channel.cs`
**Depends on**: None
**Reuses**: none — first enum in a new `Enums/` folder
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Channel` enum with `Email = 0`, `Sms = 1`, `Push = 2`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add Channel enum`

---

### T03: `NotificationStatus` enum [P]

**What**: Full lifecycle enum: `Pending`, `Rendering`, `Dispatching`, `Sent`, `Failed`, `Suppressed`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Enums/NotificationStatus.cs`
**Depends on**: None
**Reuses**: none
**Requirement**: NOTIF-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `NotificationStatus` enum with all 6 values in the order defined in design.md
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add NotificationStatus enum`

---

### T04: Domain events (`IDomainEvent` + 4 event records) [P]

**What**: Marker interface `IDomainEvent { DateTime OccurredAt }` + `NotificationDispatched`, `NotificationDelivered`, `NotificationFailed` (with `Reason`), `NotificationSuppressed` — all `sealed record`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Events/IDomainEvent.cs` + one file per event in the same folder
**Depends on**: None
**Reuses**: none — first domain events in this codebase
**Requirement**: NOTIF-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `IDomainEvent` interface with `DateTime OccurredAt`
- [ ] `NotificationDispatched(Guid NotificationId, Guid CorrelationId, DateTime OccurredAt)`
- [ ] `NotificationDelivered(Guid NotificationId, Guid CorrelationId, DateTime OccurredAt)`
- [ ] `NotificationFailed(Guid NotificationId, Guid CorrelationId, string Reason, DateTime OccurredAt)`
- [ ] `NotificationSuppressed(Guid NotificationId, Guid CorrelationId, DateTime OccurredAt)`
- [ ] All implement `IDomainEvent`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add domain events for notification lifecycle`

---

### T05: `NotificationErrorCodes` constants [P]

**What**: Error code strings following the `"Category.Reason"` convention from `ExampleErrorCodes`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Constants/NotificationErrorCodes.cs`
**Depends on**: None
**Reuses**: `Domain/Constants/ExampleErrorCodes.cs` pattern
**Requirement**: NOTIF-01, NOTIF-02, NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Constants for: `InvalidTransition`, `AlreadyTerminal`, `ChannelNotImplemented`, `InvalidPayload`, `InvalidEmailAddress`, `InvalidTemplateId`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add NotificationErrorCodes constants`

---

### T06: `EmailAddress` value object [P]

**What**: `ErrorOr<EmailAddress> Create(string value)` validating via `System.Net.Mail.MailAddress` parse
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/ValueObjects/EmailAddress.cs`
**Depends on**: T01 (test project), T05 (error codes)
**Reuses**: `System.Net.Mail.MailAddress` (BCL, no new package)
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Create("user@example.com")` returns a valid `EmailAddress` with `Value` set
- [ ] `Create("not-an-email")` returns `ErrorOr` validation error using `NotificationErrorCodes.InvalidEmailAddress`
- [ ] `Create("")` / `Create(null)` returns a validation error, does not throw
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 3+ tests pass (valid case, invalid format, empty/null)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `EmailAddress` tests pass, 0 failures

**Commit**: `feat(domain): add EmailAddress value object`

---

### T07: `TemplateId` value object [P]

**What**: `ErrorOr<TemplateId> Create(string value)` rejecting null/whitespace only (no existence check — that's E-04)
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/ValueObjects/TemplateId.cs`
**Depends on**: T01, T05
**Reuses**: none
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Create("welcome-email")` returns a valid `TemplateId`
- [ ] `Create("")` / `Create(null)` / `Create("   ")` returns a validation error using `NotificationErrorCodes.InvalidTemplateId`
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 2+ tests pass

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `TemplateId` tests pass, 0 failures

**Commit**: `feat(domain): add TemplateId value object`

---

### T08: `ConsentPreference` value object [P]

**What**: `ErrorOr<ConsentPreference> Create(Guid recipientId, Channel channel, bool optedIn, DateTime updatedAt)`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/ValueObjects/ConsentPreference.cs`
**Depends on**: T01, T02 (`Channel` enum)
**Reuses**: none — first VO of its kind
**Requirement**: NOTIF-04

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Create` with a valid, non-empty `recipientId` succeeds and exposes `RecipientId`, `Channel`, `OptedIn`, `UpdatedAt`
- [ ] `Create` with `Guid.Empty` as `recipientId` returns a validation error
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 2+ tests pass

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ConsentPreference` tests pass, 0 failures

**Commit**: `feat(domain): add ConsentPreference value object`

---

### T09: `ConsentDecision` [P]

**What**: `NoRecordFound()` (→ `IsSuppressed = false`, confirmed 2026-07-13 default-opt-in for transactional channels) and `FromPreference(ConsentPreference)` (→ `IsSuppressed = !preference.OptedIn`)
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/ValueObjects/ConsentDecision.cs`
**Depends on**: T08
**Reuses**: `ConsentPreference` (T08)
**Requirement**: NOTIF-04

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `ConsentDecision.NoRecordFound().IsSuppressed` is `false`
- [ ] `ConsentDecision.FromPreference(preference with OptedIn=true).IsSuppressed` is `false`
- [ ] `ConsentDecision.FromPreference(preference with OptedIn=false).IsSuppressed` is `true`
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 3 tests pass (matches the 3 `Done when` assertions above)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ConsentDecision` tests pass, 0 failures

**Commit**: `feat(domain): add ConsentDecision encoding default opt-in rule`

---

### T10: `IConsentRepository` interface [P]

**What**: `Task<ConsentPreference?> FindAsync(Guid recipientId, Channel channel, CancellationToken ct = default)` — `null` is the explicit "no record" case
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/IConsentRepository.cs`
**Depends on**: T08, T02
**Reuses**: none
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Interface compiles with the signature above
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add IConsentRepository contract`

---

### T11: `ITemplateRenderer` interface [P]

**What**: `Task<ErrorOr<string>> RenderAsync(TemplateId templateId, IReadOnlyDictionary<string, string> payload, CancellationToken ct = default)`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/ITemplateRenderer.cs`
**Depends on**: T07
**Reuses**: none
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Interface compiles with the signature above, no Scriban types in the signature
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add ITemplateRenderer contract`

---

### T12: `IEmailSender` interface [P]

**What**: `Task<ErrorOr<Success>> SendAsync(EmailAddress recipient, string renderedContent, CancellationToken ct = default)`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/IEmailSender.cs`
**Depends on**: T06
**Reuses**: none
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Interface compiles with the signature above
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add IEmailSender contract`

---

### T13: `Notification.Create()` factory

**What**: `static ErrorOr<Notification> Create(Guid correlationId, Guid recipientId, EmailAddress recipient, Channel channel, TemplateId templateId, IReadOnlyDictionary<string, string> payload)` — starts in `Pending`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Entities/Notification.cs` (new file)
**Depends on**: T02, T03, T05, T06, T07
**Reuses**: `Entities/ExampleEntity.cs` folder/namespace convention (private constructor + static factory)
**Requirement**: NOTIF-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Create(...)` with valid inputs returns a `Notification` with `Status == Pending`, `Id` auto-generated, `CreatedAt` set
- [ ] `Create(...)` with an empty `payload` dictionary returns a validation error (`NotificationErrorCodes.InvalidPayload`) rather than constructing
- [ ] `DomainEvents` is empty immediately after `Create` (no event raised on construction — only `Dispatch`/`MarkSent`/`MarkFailed` raise events)
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 3+ tests pass

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `Notification.Create` tests pass, 0 failures

**Commit**: `feat(domain): add Notification aggregate with Create factory`

---

### T14: `Notification.Dispatch()`

**What**: `ErrorOr<Success> Dispatch(ConsentDecision consent, bool isPayloadValid)` — sequences `Pending` → `Rendering` → `Dispatching` (raises `NotificationDispatched`), or short-circuits to `Suppressed` (raises `NotificationSuppressed`) if `consent.IsSuppressed`, or rejects with `ChannelNotImplemented` for `Sms`/`Push`, or rejects with `InvalidPayload` if `!isPayloadValid`
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Entities/Notification.cs` (modify — add method)
**Depends on**: T13, T04, T09
**Reuses**: `NotificationErrorCodes` (T05), domain events (T04)
**Requirement**: NOTIF-02, NOTIF-04, NOTIF-05

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Dispatch(ConsentDecision.NoRecordFound(), isPayloadValid: true)` on a `Pending`+`Email` notification → `Status == Dispatching`, `NotificationDispatched` raised
- [ ] `Dispatch` with `consent.IsSuppressed == true` → `Status == Suppressed`, `NotificationSuppressed` raised, method returns success (not an error — NOTIF-04 edge case)
- [ ] `Dispatch` with `isPayloadValid: false` → returns validation error, `Status` remains `Pending`
- [ ] `Dispatch` on a notification with `Channel.Sms` or `Channel.Push` → returns `ChannelNotImplemented` error, `Status` remains `Pending`
- [ ] `Dispatch` called twice on the same instance (already `Dispatching`) → second call returns an error, does not re-raise events
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 5+ tests pass (matches the 5 `Done when` assertions above)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `Notification.Dispatch` tests pass, 0 failures

**Commit**: `feat(domain): add Dispatch with consent enforcement and channel guard`

---

### T15: `Notification.MarkSent()` / `Notification.MarkFailed()`

**What**: `ErrorOr<Success> MarkSent()` (`Dispatching` → `Sent`, raises `NotificationDelivered`) and `ErrorOr<Success> MarkFailed(string reason)` (`Dispatching` → `Failed`, raises `NotificationFailed`, stores `FailureReason`)
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Entities/Notification.cs` (modify — add 2 methods)
**Depends on**: T14
**Reuses**: `NotificationErrorCodes` (T05), domain events (T04)
**Requirement**: NOTIF-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `MarkSent()` on a `Dispatching` notification → `Status == Sent`, `NotificationDelivered` raised
- [ ] `MarkFailed("SES throttled")` on a `Dispatching` notification → `Status == Failed`, `FailureReason == "SES throttled"`, `NotificationFailed` raised with matching `Reason`
- [ ] `MarkSent()` / `MarkFailed()` called on a `Pending`, `Sent`, `Failed`, or `Suppressed` notification (i.e., any non-`Dispatching` state) → returns `AlreadyTerminal` or `InvalidTransition` error, no mutation, no event raised
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 6+ tests pass (2 happy path + 4 illegal-state variants minimum)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `Notification.MarkSent`/`MarkFailed` tests pass, 0 failures

**Commit**: `feat(domain): add MarkSent and MarkFailed terminal transitions`

---

### T16: `INotificationRepository` interface

**What**: `SaveIfNotExistsAsync`, `GetByIdAsync`, `GetByRecipientAsync`, `UpdateStatusAsync` — contract only, no implementation (E-04)
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/INotificationRepository.cs`
**Depends on**: T15, T03
**Reuses**: none — deliberately not composed from `Interfaces/Common/*` (see design.md Tech Decisions)
**Requirement**: NOTIF-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Task<bool> SaveIfNotExistsAsync(Notification notification, CancellationToken ct = default)`
- [ ] `Task<Notification?> GetByIdAsync(Guid id, CancellationToken ct = default)`
- [ ] `Task<IReadOnlyList<Notification>> GetByRecipientAsync(Guid recipientId, CancellationToken ct = default)`
- [ ] `Task UpdateStatusAsync(Guid id, NotificationStatus status, CancellationToken ct = default)`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add INotificationRepository contract`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  ├── T01 [P]  Tests.Domain project
  ├── T02 [P]  Channel enum
  ├── T03 [P]  NotificationStatus enum
  ├── T04 [P]  Domain events
  └── T05 [P]  NotificationErrorCodes

Phase 2 (Parallel, after T01+T02):
  ├── T06 [P]  EmailAddress VO
  ├── T07 [P]  TemplateId VO
  └── T08 [P]  ConsentPreference VO

Phase 3 (Parallel, after Phase 2):
  ├── T09 [P]  ConsentDecision
  ├── T10 [P]  IConsentRepository
  ├── T11 [P]  ITemplateRenderer
  └── T12 [P]  IEmailSender

Phase 4 (Sequential — same file):
  T13 ──→ T14 ──→ T15

Phase 5 (Sequential, after Phase 4):
  T16
```

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T01: Tests.Domain scaffold | 1 project | ✅ Granular |
| T02: Channel enum | 1 enum | ✅ Granular |
| T03: NotificationStatus enum | 1 enum | ✅ Granular |
| T04: Domain events | 5 tiny records, cohesive (one concept: event buffer) | ✅ Granular (2-3 related things rule) |
| T05: NotificationErrorCodes | 1 file, 1 concept | ✅ Granular |
| T06: EmailAddress VO | 1 VO | ✅ Granular |
| T07: TemplateId VO | 1 VO | ✅ Granular |
| T08: ConsentPreference VO | 1 VO | ✅ Granular |
| T09: ConsentDecision | 1 type | ✅ Granular |
| T10: IConsentRepository | 1 interface | ✅ Granular |
| T11: ITemplateRenderer | 1 interface | ✅ Granular |
| T12: IEmailSender | 1 interface | ✅ Granular |
| T13: Notification.Create() | 1 method (on new aggregate file) | ✅ Granular |
| T14: Notification.Dispatch() | 1 method | ✅ Granular |
| T15: MarkSent/MarkFailed | 2 tightly-coupled terminal transitions, same concept | ✅ Granular (2-3 related things rule) |
| T16: INotificationRepository | 1 interface | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T01 | None | Phase 1, no incoming arrow | ✅ Match |
| T02 | None | Phase 1, no incoming arrow | ✅ Match |
| T03 | None | Phase 1, no incoming arrow | ✅ Match |
| T04 | None | Phase 1, no incoming arrow | ✅ Match |
| T05 | None | Phase 1, no incoming arrow | ✅ Match |
| T06 | T01, T05 | T01 → T06 (T05 same-phase, both Phase 1 outputs feed Phase 2) | ✅ Match |
| T07 | T01, T05 | T01 → T07 (T05 same-phase feed) | ✅ Match |
| T08 | T01, T02 | T01, T02 → T08 | ✅ Match |
| T09 | T08 | T08 → T09 | ✅ Match |
| T10 | T08, T02 | T08, T02 → T10 | ✅ Match |
| T11 | T07 | T07 → T11 | ✅ Match |
| T12 | T06 | T06 → T12 | ✅ Match |
| T13 | T02, T03, T05, T06, T07 | All → T13 | ✅ Match |
| T14 | T13, T04, T09 | T13, T04, T09 → T14 | ✅ Match |
| T15 | T14 | T14 → T15 | ✅ Match |
| T16 | T15, T03 | T15, T03 → T16 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T01 | Test project scaffold | none | none | ✅ OK |
| T02 | Enum | none | none | ✅ OK |
| T03 | Enum | none | none | ✅ OK |
| T04 | Domain event records | none | none | ✅ OK |
| T05 | Constants | none | none | ✅ OK |
| T06 | Value Object | unit | unit | ✅ OK |
| T07 | Value Object | unit | unit | ✅ OK |
| T08 | Value Object | unit | unit | ✅ OK |
| T09 | Value Object | unit | unit | ✅ OK |
| T10 | Interface (no impl) | none | none | ✅ OK |
| T11 | Interface (no impl) | none | none | ✅ OK |
| T12 | Interface (no impl) | none | none | ✅ OK |
| T13 | Aggregate method | unit | unit | ✅ OK |
| T14 | Aggregate method | unit | unit | ✅ OK |
| T15 | Aggregate methods | unit | unit | ✅ OK |
| T16 | Interface (no impl) | none | none | ✅ OK |

---

## Requirement Traceability Update

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| NOTIF-01 | P1: NOTIF-US-01 | Tasks | Mapped → T03, T05, T13, T15 |
| NOTIF-02 | P1: NOTIF-US-01 | Tasks | Mapped → T04, T14 |
| NOTIF-03 | P1: NOTIF-US-01 | Tasks | Mapped → T01 |
| NOTIF-04 | P1: NOTIF-US-02 | Tasks | Mapped → T08, T09, T14 |
| NOTIF-05 | P1: NOTIF-US-02 | Tasks | Mapped → T14 |
| NOTIF-06 | P1: NOTIF-US-03 | Tasks | Mapped → T02, T05, T06, T07, T10, T11, T12, T16 |
| NOTIF-07 | P1: NOTIF-US-01 | Tasks | Mapped → T04 (event buffer, covered by `DomainEvents` assertion in T13/T14/T15) |

**Coverage:** 7 total, 7 mapped to tasks, 0 unmapped ✅
