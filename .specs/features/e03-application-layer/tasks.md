# E-03 · Application Layer — Use Cases Tasks

**Design**: `.specs/features/e03-application-layer/design.md`
**Status**: Draft

---

## Test Strategy (reusing gate commands from `.specs/features/e01-foundation/tasks.md` / E-02)

| Gate | Command | When |
| --- | --- | --- |
| **build** | `dotnet build --no-incremental` | Records, constants, resource files with no branching logic |
| **quick** | `dotnet test --filter "Category!=Integration"` | Unit tests only (this epic has zero real I/O — Moq stands in for all four Domain contracts) |

## Test Coverage Matrix

| Code Layer | Test Type Required | Parallel-Safe |
| --- | --- | --- |
| `DispatchValidationErrorCodes` constants | none | Yes |
| `ValidationMessageResource` new keys | none (exercised via validator tests) | Yes |
| `DispatchNotificationRequest`, `DispatchOutcome` records | none (no logic) | Yes |
| `DispatchNotificationValidator` | unit | Yes |
| `DispatchNotificationHandler` | unit | Yes — pure Moq, no shared fixtures |
| IoC registration | none (build-only, exercised transitively by DI resolution in handler/consumer tests) | Yes |
| `NotificationRequestedConsumer` (modified) | unit | Yes — existing `Tests.Api` pattern already mocks `IConsumer<,>`/`IKafkaConsumerFactory` |

**Parallelism Assessment**: Same as E-02 — all unit tests here are in-process xUnit + Moq, no Testcontainers, no shared mutable state. Every `[P]` below is safe.

---

## Execution Plan

```
Phase 1 — Foundation (Parallel):
  T01 [P]  DispatchValidationErrorCodes constants
  T02 [P]  ValidationMessageResource new keys
  T03 [P]  DispatchNotificationRequest record
  T04 [P]  DispatchOutcome record

Phase 2 — Validator (Sequential, after T01-T03):
  T01, T02, T03 ──→ T05  DispatchNotificationValidator + tests

Phase 3 — Handler (Sequential — same file evolves across 3 tasks):
  T03, T04, T05 ──→ T06  Handler: validate → construct → idempotency short-circuit
  T06 ──→ T07  Handler: consent resolution → Dispatch → suppressed path
  T07 ──→ T08  Handler: render → persist Dispatching → send → mark final

Phase 4 — Wiring (Sequential, after Handler complete):
  T05, T08 ──→ T09  IoC registration
  T08, T09 ──→ T10  NotificationRequestedConsumer wiring + tests
```

---

## Task Breakdown

### T01: `DispatchValidationErrorCodes` constants [P]

**What**: Error code constants for wire-level validation failures, following the `NotificationErrorCodes` pattern
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Constants/DispatchValidationErrorCodes.cs`
**Depends on**: None
**Reuses**: `Domain/Constants/NotificationErrorCodes.cs` pattern (same precedent `CreateExampleValidator` follows — validation constants live in `Domain/Constants` even when only Application-layer validators reference them)
**Requirement**: DISPATCH-01, DISPATCH-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Constants: `CorrelationIdRequired`, `RecipientIdRequired`, `RecipientEmailRequired`, `TemplateIdRequired`, `InvalidChannel`, `PayloadRequired`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(application): add DispatchValidationErrorCodes constants`

---

### T02: `ValidationMessageResource` new keys [P]

**What**: New resource keys for the wire-validation messages, added to the existing `.resx` (default + `pt-BR`), following the `NAME_REQUIRED`-style precedent
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/MessageResource/ValidationMessageResource.resx`, `ValidationMessageResource.pt-BR.resx`, `ValidationMessageResource.Designer.cs`
**Depends on**: None
**Reuses**: Existing resx/Designer generation pattern — no new i18n mechanism
**Requirement**: DISPATCH-01, DISPATCH-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Keys added: `CORRELATION_ID_REQUIRED`, `RECIPIENT_ID_REQUIRED`, `RECIPIENT_EMAIL_REQUIRED`, `TEMPLATE_ID_REQUIRED`, `CHANNEL_INVALID`, `PAYLOAD_REQUIRED` in both the default and `pt-BR` resx
- [ ] `Designer.cs` regenerated (or hand-added, matching existing generated members exactly) so the new keys are accessible as `ValidationMessageResource.CORRELATION_ID_REQUIRED` etc.
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`; a quick `ValidationMessageResource.CORRELATION_ID_REQUIRED` reference compiles

**Commit**: `feat(application): add wire-validation messages to ValidationMessageResource`

---

### T03: `DispatchNotificationRequest` record [P]

**What**: The deserialized wire-message shape
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/Request/DispatchNotificationRequest.cs`
**Depends on**: None
**Reuses**: `CreateExampleRequest`'s record-DTO pattern
**Requirement**: DISPATCH-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `sealed record DispatchNotificationRequest(Guid CorrelationId, Guid RecipientId, string RecipientEmail, string Channel, string TemplateId, IReadOnlyDictionary<string, string> Payload)`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(application): add DispatchNotificationRequest`

---

### T04: `DispatchOutcome` record [P]

**What**: The handler's result type distinguishing terminal status from duplicate no-op
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/DispatchOutcome.cs`
**Depends on**: None (references `NotificationStatus`, already merged in E-02)
**Reuses**: none
**Requirement**: DISPATCH-03, DISPATCH-04

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `sealed record DispatchOutcome(NotificationStatus Status, bool WasDuplicate)`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(application): add DispatchOutcome`

---

### T05: `DispatchNotificationValidator`

**What**: FluentValidation rules for `DispatchNotificationRequest` — non-empty required fields, recognized `Channel` name
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/Validator/DispatchNotificationValidator.cs`
**Depends on**: T01, T02, T03
**Reuses**: `CreateExampleValidator`'s structure (`AbstractValidator<T>`, `.WithMessage(ValidationMessageResource.X)`)
**Requirement**: DISPATCH-01, DISPATCH-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `CorrelationId` / `RecipientId` — not `Guid.Empty`
- [ ] `RecipientEmail` / `TemplateId` — not empty string
- [ ] `Channel` — must parse via `Enum.TryParse<Channel>(x.Channel, ignoreCase: true, out _)`
- [ ] `Payload` — not null and not empty
- [ ] All rules use `DispatchValidationErrorCodes` + `ValidationMessageResource` (no inline magic strings, per `CLAUDE.md`)
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 6+ tests pass (one valid case + one failing case per field, in `03-tests/02-Validators/RentifyxCommunications.Tests.Validators/Features/Notifications/DispatchNotificationValidatorTests.cs`)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `DispatchNotificationValidator` tests pass, 0 failures

**Commit**: `feat(application): add DispatchNotificationValidator`

---

### T06: `DispatchNotificationHandler` — validate, construct, idempotency short-circuit

**What**: First slice of the handler: run the validator, convert wire fields into Domain VOs (`EmailAddress`, `TemplateId`, `Channel` parse) and `NotificationEntity.Create(...)`, then `SaveIfNotExistsAsync` — short-circuit on duplicate
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/DispatchNotificationHandler.cs` (new file)
**Depends on**: T03, T04, T05
**Reuses**: `ValidationExtensions.ValidateToErrorsAsync`, primary-constructor DI style (`CLAUDE.md`)
**Requirement**: DISPATCH-01, DISPATCH-02, DISPATCH-03, DISPATCH-04

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Invalid request (per `DispatchNotificationValidator`) → returns validation errors, no repository/consent/render/send mock invoked
- [ ] Valid request with an invalid `RecipientEmail` format (passes structural validator but fails `EmailAddress.Create`) → returns a validation error, no further calls
- [ ] Valid request, `SaveIfNotExistsAsync` mocked to return `false` → returns `DispatchOutcome(Pending, WasDuplicate: true)`, and `IConsentRepository`/`ITemplateRenderer`/`IEmailSender` mocks verified `Times.Never`
- [ ] Valid request, `SaveIfNotExistsAsync` mocked to return `true` → proceeds (asserted via a `NotImplementedException` placeholder or a mocked next-step call, since T07 adds the rest of the sequence in the same file — see note below)
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 4+ tests pass

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all tests in `DispatchNotificationHandlerTests.cs` pass, 0 failures

**Note**: This task produces a handler that only implements steps 1-4 of the design's 11-step sequence; the method body is completed by T07/T08 in the same file, matching the pattern E-02 used for `NotificationEntity.Dispatch`/`MarkSent` across sequential tasks. Do not stub the remaining steps with a `throw new NotImplementedException()` left uncommitted — T07 replaces that immediately after.

**Commit**: `feat(application): add DispatchNotificationHandler validation and idempotency slice`

---

### T07: `DispatchNotificationHandler` — consent resolution, Dispatch, suppressed path

**What**: Second slice: `IConsentRepository.FindAsync` → `ConsentDecision` → `notification.Dispatch(consent, isPayloadValid: true)` → if `Suppressed`, persist and return early
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/DispatchNotificationHandler.cs` (modify)
**Depends on**: T06
**Reuses**: `ConsentDecision.NoRecordFound()`/`FromPreference()` (E-02)
**Requirement**: DISPATCH-05 (partial), DISPATCH-07, DISPATCH-08

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `IConsentRepository.FindAsync` mocked to return `null` → `ConsentDecision.NoRecordFound()` used, dispatch proceeds (verified by falling through to whatever T07 currently does after — a mocked next-step or the render call once T08 lands)
- [ ] `IConsentRepository.FindAsync` mocked to return a `ConsentPreference` with `OptedIn = false` → handler calls `UpdateStatusAsync(id, Suppressed)`, returns `DispatchOutcome(Suppressed, false)`, and `ITemplateRenderer`/`IEmailSender` mocks verified `Times.Never`
- [ ] `IConsentRepository.FindAsync` mocked to return a `ConsentPreference` with `OptedIn = true` → dispatch proceeds normally
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 3+ new tests pass (total test count in the file grows from T06's baseline)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `DispatchNotificationHandlerTests` pass, 0 failures

**Commit**: `feat(application): add consent resolution and suppressed path to DispatchNotificationHandler`

---

### T08: `DispatchNotificationHandler` — render, persist Dispatching, send, mark final

**What**: Final slice: `ITemplateRenderer.RenderAsync` → on failure `MarkFailed` + persist; on success `UpdateStatusAsync(Dispatching)` → `IEmailSender.SendAsync` → `MarkSent`/`MarkFailed` + persist final status
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/DispatchNotificationHandler.cs` (modify — completes the method)
**Depends on**: T07
**Reuses**: none new
**Requirement**: DISPATCH-05, DISPATCH-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `RenderAsync` mocked to fail → `MarkFailed` called, `UpdateStatusAsync(id, Failed)` called, `IEmailSender.SendAsync` verified `Times.Never`, returns `DispatchOutcome(Failed, false)`
- [ ] `RenderAsync` succeeds → `UpdateStatusAsync(id, Dispatching)` called BEFORE `SendAsync` (assert via Moq `MockSequence` or `Callback`-based ordering)
- [ ] `SendAsync` mocked to succeed → `MarkSent` called, `UpdateStatusAsync(id, Sent)` called, returns `DispatchOutcome(Sent, false)`
- [ ] `SendAsync` mocked to fail → `MarkFailed` called, `UpdateStatusAsync(id, Failed)` called, returns `DispatchOutcome(Failed, false)` — no exception thrown
- [ ] Full happy-path test asserts the exact call order end-to-end: `SaveIfNotExists` → `FindAsync` → `RenderAsync` → `UpdateStatusAsync(Dispatching)` → `SendAsync` → `UpdateStatusAsync(Sent)`
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 5+ new tests pass

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `DispatchNotificationHandlerTests` pass, 0 failures, full sequence covered

**Commit**: `feat(application): complete DispatchNotificationHandler outbox sequence`

---

### T09: IoC registration

**What**: Register `IValidator<DispatchNotificationRequest>` and `IHandler<DispatchNotificationRequest, DispatchOutcome>` as `Scoped`
**Where**: `02-src/04-IoC/RentifyxCommunications.IoC/ApplicationDependencyInjection.cs` (modify)
**Depends on**: T05, T08
**Reuses**: Existing `Examples` registration block style
**Requirement**: DISPATCH-01 through DISPATCH-08 (wiring, not new behavior)

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `services.AddScoped<IValidator<DispatchNotificationRequest>, DispatchNotificationValidator>();`
- [ ] `services.AddScoped<IHandler<DispatchNotificationRequest, DispatchOutcome>, DispatchNotificationHandler>();`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(ioc): register DispatchNotificationValidator and DispatchNotificationHandler`

---

### T10: `NotificationRequestedConsumer` wiring

**What**: Replace the E-01 skeleton's "no processing logic yet" comment with real message processing — deserialize, resolve a per-message `IServiceScope`, call the handler, log outcome, always commit
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Consumers/NotificationRequestedConsumer.cs` (modify)
**Depends on**: T08, T09
**Reuses**: Existing `StartAsync`/`StopAsync`/`Dispose` lifecycle (unchanged); existing `Tests.Api/Consumers/NotificationRequestedConsumerTests.cs` mocking pattern (`Mock<IConsumer<Ignore, string>>`, `Mock<IKafkaConsumerFactory>`, `ListLogger`)
**Requirement**: DISPATCH-01 (AC2), DISPATCH-09

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Constructor gains `IServiceScopeFactory scopeFactory` parameter
- [ ] `ConsumeLoop` becomes an instance `async Task` method; still runs inside the existing `Task.Run(...)` in `StartAsync`
- [ ] Malformed JSON → logged at `Error` with raw payload + partition/offset, offset committed, loop continues to the next message
- [ ] Handler throws an unexpected exception → caught, logged at `Error` with `correlationId` if the request was parsed, offset committed, loop continues
- [ ] Handler returns successfully (any `DispatchOutcome`) → logged at `Information` with the resulting status, offset committed
- [ ] A malformed message followed by a valid one → the valid message is still processed (no partition stall)
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: existing 3 `NotificationRequestedConsumerTests` still pass + 4+ new tests for the processing behavior above

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `NotificationRequestedConsumerTests` pass (7+ total), 0 failures

**Commit**: `feat(api): wire NotificationRequestedConsumer to DispatchNotificationHandler`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  ├── T01 [P]  DispatchValidationErrorCodes
  ├── T02 [P]  ValidationMessageResource keys
  ├── T03 [P]  DispatchNotificationRequest
  └── T04 [P]  DispatchOutcome

Phase 2 (Sequential, after Phase 1):
  T05  DispatchNotificationValidator + tests

Phase 3 (Sequential — same file):
  T06 ──→ T07 ──→ T08

Phase 4 (Sequential, after Phase 3):
  T09 ──→ T10
```

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T01: DispatchValidationErrorCodes | 1 file, 1 concept | ✅ Granular |
| T02: ValidationMessageResource keys | 1 concept (i18n keys), 3 files (resx×2 + designer) | ✅ Granular (cohesive) |
| T03: DispatchNotificationRequest | 1 record | ✅ Granular |
| T04: DispatchOutcome | 1 record | ✅ Granular |
| T05: DispatchNotificationValidator | 1 validator | ✅ Granular |
| T06: Handler validate/construct/idempotency slice | 1 method, first third | ✅ Granular |
| T07: Handler consent/dispatch slice | 1 method, second third | ✅ Granular |
| T08: Handler render/send/final slice | 1 method, final third | ✅ Granular |
| T09: IoC registration | 2 lines, 1 file | ✅ Granular |
| T10: Consumer wiring | 1 method (`ConsumeLoop`) + constructor param | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T01 | None | Phase 1, no incoming arrow | ✅ Match |
| T02 | None | Phase 1, no incoming arrow | ✅ Match |
| T03 | None | Phase 1, no incoming arrow | ✅ Match |
| T04 | None | Phase 1, no incoming arrow | ✅ Match |
| T05 | T01, T02, T03 | T01, T02, T03 → T05 | ✅ Match |
| T06 | T03, T04, T05 | T03, T04, T05 → T06 | ✅ Match |
| T07 | T06 | T06 → T07 | ✅ Match |
| T08 | T07 | T07 → T08 | ✅ Match |
| T09 | T05, T08 | T05, T08 → T09 | ✅ Match |
| T10 | T08, T09 | T08, T09 → T10 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T01 | Constants | none | none | ✅ OK |
| T02 | Resource keys | none | none | ✅ OK |
| T03 | Record | none | none | ✅ OK |
| T04 | Record | none | none | ✅ OK |
| T05 | Validator | unit | unit | ✅ OK |
| T06 | Handler slice | unit | unit | ✅ OK |
| T07 | Handler slice | unit | unit | ✅ OK |
| T08 | Handler slice | unit | unit | ✅ OK |
| T09 | IoC registration | none | none | ✅ OK |
| T10 | Consumer | unit | unit | ✅ OK |

---

## Requirement Traceability Update

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| DISPATCH-01 | P1: DISPATCH-US-01 | Tasks | Mapped → T01, T02, T03, T05, T06, T10 |
| DISPATCH-02 | P1: DISPATCH-US-01 | Tasks | Mapped → T01, T02, T05, T06 |
| DISPATCH-03 | P1: DISPATCH-US-02 | Tasks | Mapped → T04, T06 |
| DISPATCH-04 | P1: DISPATCH-US-02 | Tasks | Mapped → T06 |
| DISPATCH-05 | P1: DISPATCH-US-03 | Tasks | Mapped → T07, T08 |
| DISPATCH-06 | P1: DISPATCH-US-03 | Tasks | Mapped → T08 |
| DISPATCH-07 | P1: DISPATCH-US-04 | Tasks | Mapped → T07 |
| DISPATCH-08 | P1: DISPATCH-US-04 | Tasks | Mapped → T07 |
| DISPATCH-09 | P1: DISPATCH-US-05 | Tasks | Mapped → T10 |

**Coverage:** 9 total, 9 mapped to tasks, 0 unmapped ✅
