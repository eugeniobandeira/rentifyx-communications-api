# E-04 · F-07 — SES & DynamoDB Integration Tasks

**Design**: `.specs/features/e04-f07-ses-dynamodb/design.md`
**Status**: Draft

---

## Test Strategy (reusing gate commands from E-01/E-02/E-03; Testcontainers precedent from E-01 T12)

| Gate | Command | When |
| --- | --- | --- |
| **build** | `dotnet build --no-incremental` | Package additions, IoC registration |
| **quick** | `dotnet test --filter "Category!=Integration"` | Pure unit tests (Scriban rendering, MockEmailSender) |
| **full** | `dotnet test` | Includes LocalStack Testcontainers integration tests |

## Test Coverage Matrix

| Code Layer | Test Type Required | Parallel-Safe |
| --- | --- | --- |
| Package additions (`Directory.Packages.props`) | none | Yes |
| `LocalStackNotificationInfrastructureFixture` | none (it's test infrastructure itself, exercised by the tests that consume it) | N/A |
| `DynamoDbNotificationRepository` | integration | **No** — shared LocalStack container/table per E-01's Testcontainers precedent |
| `DynamoDbConsentRepository` | integration | **No** — same shared container |
| `ScribanTemplateRenderer` | unit | Yes — pure, embedded resources only, no I/O |
| `SesEmailSender` | integration | **No** — same shared container |
| `MockEmailSender` | unit | Yes — no I/O by definition |
| IoC registration | none | Yes |

**Parallelism Assessment**: Same rule as E-01 T12 — Testcontainers-based integration tests share container lifecycle and are NOT parallel-safe. Pure unit tests (Scriban, Mock) have zero shared state and are parallel-safe.

---

## Execution Plan

```
Phase 1 — Foundation (Parallel):
  T01 [P]  Add AWSSDK.DynamoDBv2 / AWSSDK.SimpleEmail / Scriban package versions
  T02 [P]  LocalStackNotificationInfrastructureFixture (creates notifications table + GSI1/GSI2)
  T06 [P]  ScribanTemplateRenderer + welcome-email.scriban + unit tests
  T08 [P]  MockEmailSender + unit tests

Phase 2 — DynamoDB Repositories (Sequential within each file, parallel across files):
  T01, T02 ──┬──→ T03  DynamoDbNotificationRepository: SaveIfNotExistsAsync + GetByIdAsync (GSI2)
             │        T03 ──→ T04  DynamoDbNotificationRepository: GetByRecipientAsync (GSI1) + UpdateStatusAsync + TTL
             └──→ T05 [P]  DynamoDbConsentRepository + integration tests

Phase 3 — SES (after Foundation):
  T01, T02 ──→ T07  SesEmailSender + integration tests

Phase 4 — Wiring (Sequential, after everything else):
  T04, T05, T06, T07, T08 ──→ T09  IoC registration
```

---

## Task Breakdown

### T01: Add DynamoDB/SES/Scriban package versions [P]

**What**: Add `AWSSDK.DynamoDBv2`, `AWSSDK.SimpleEmail`, and `Scriban` to `Directory.Packages.props`; reference them in `RentifyxCommunications.Infrastructure.csproj`
**Where**: `Directory.Packages.props`, `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/RentifyxCommunications.Infrastructure.csproj`
**Depends on**: None
**Reuses**: Existing `AWSSDK.SecretsManager`/`AWSSDK.Extensions.NETCore.Setup` version-pinning pattern
**Requirement**: SESDB-01 through SESDB-08 (all depend on these packages existing)

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `AWSSDK.DynamoDBv2`, `AWSSDK.SimpleEmail`, `Scriban` versions pinned in `Directory.Packages.props`
- [ ] `RentifyxCommunications.Infrastructure.csproj` references all three (no version attribute — centrally managed)
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `chore(infrastructure): add DynamoDB, SES, and Scriban package references`

---

### T02: `LocalStackNotificationInfrastructureFixture` [P]

**What**: Test-only fixture — one shared LocalStack container exposing `IAmazonDynamoDB` and `IAmazonSimpleEmailService`, creating the `notifications` table (with `GSI1`/`GSI2`) in `InitializeAsync`
**Where**: `03-tests/05-Integration/RentifyxCommunications.Tests.Integration/Infrastructure/LocalStackNotificationInfrastructureFixture.cs`
**Depends on**: T01
**Reuses**: `LocalStackSecretsManagerFixture`'s `IAsyncLifetime` + `LocalStackBuilder` shape (`Tests.Integration/Secrets/LocalStackSecretsManagerFixture.cs`)
**Requirement**: SESDB-01, SESDB-03, SESDB-06

**Tools**:
- MCP: context7 (AWSSDK.DynamoDBv2 `CreateTableAsync` API — confirm exact request shape for GSIs before writing)
- Skill: none

**Done when**:
- [ ] `LocalStackContainer` started with both `dynamodb` and `ses` services enabled
- [ ] `IAmazonDynamoDB DynamoDb { get; }` and `IAmazonSimpleEmailService Ses { get; }` exposed
- [ ] `notifications` table created via `CreateTableAsync` with `PK`/`SK` as the primary key, `GSI1` (`GSI1PK`/`GSI1SK`) and `GSI2` (`GSI2PK`/`GSI2SK`) as global secondary indexes, on-demand billing mode
- [ ] `[CollectionDefinition(nameof(NotificationInfrastructureFixtureGroup))]` + `ICollectionFixture<...>` registered, matching the `LocalStackFixtureGroup` precedent
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `test(integration): add LocalStack fixture for DynamoDB and SES`

---

### T03: `DynamoDbNotificationRepository` — SaveIfNotExistsAsync + GetByIdAsync

**What**: First slice — atomic conditional-write save (`PK=NOTIF#{correlationId}`, per corrected AD-008) and lookup by `Id` via `GSI2`
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/DynamoDbNotificationRepository.cs` (new file)
**Depends on**: T01, T02
**Reuses**: `SecretsManagerProvider`'s primary-constructor shape
**Requirement**: SESDB-01, SESDB-02

**Tools**:
- MCP: context7 (AWSSDK.DynamoDBv2 `PutItemAsync`/`ConditionExpression`/`QueryAsync` exact API — confirm request/response shapes)
- Skill: none

**Done when**:
- [ ] `SaveIfNotExistsAsync` writes `PK=NOTIF#{correlationId}`, `SK=METADATA`, `GSI1PK=RECIPIENT#{recipientId}`, `GSI1SK=NOTIF#{createdAt:o}#{id}`, `GSI2PK=ID#{id}`, `GSI2SK=ID#{id}`, `Channel`/`Status` as string names, `TTL` = `CreatedAt + 90 days` (epoch seconds) — returns `true`
- [ ] `SaveIfNotExistsAsync` called twice with the same `CorrelationId` → second call catches `ConditionalCheckFailedException`, returns `false` (not thrown)
- [ ] `GetByIdAsync` queries `GSI2` by `ID#{id}` and returns a fully-hydrated `NotificationEntity`, or `null` if not found
- [ ] Gate check passes: `dotnet test --filter "Category=Integration"` (LocalStack, sequential)
- [ ] Test count: 4+ integration tests pass (save-new, duplicate-save, get-existing, get-missing)

**Tests**: integration
**Gate**: full

**Verify**: `dotnet test --filter "Category=Integration"` → all `DynamoDbNotificationRepository` tests pass, 0 failures

**Commit**: `feat(infrastructure): add DynamoDbNotificationRepository save and get-by-id`

---

### T04: `DynamoDbNotificationRepository` — GetByRecipientAsync + UpdateStatusAsync

**What**: Second slice — recipient query via `GSI1` and partial status update
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/DynamoDbNotificationRepository.cs` (modify)
**Depends on**: T03
**Reuses**: none new
**Requirement**: SESDB-01

**Tools**:
- MCP: context7 (AWSSDK.DynamoDBv2 `UpdateItemAsync` — confirm `UpdateExpression` syntax for a partial attribute update)
- Skill: none

**Done when**:
- [ ] `GetByRecipientAsync` queries `GSI1` by `RECIPIENT#{recipientId}` and returns all matching notifications, ordered by `GSI1SK` (i.e. by `CreatedAt`)
- [ ] `UpdateStatusAsync` uses `UpdateItemAsync` with an `UpdateExpression` touching only `Status` and `UpdatedAt` — verified by asserting other attributes (e.g. `Payload`) are unchanged after the call
- [ ] `UpdateStatusAsync` persists `NotificationStatus` as its string name
- [ ] Gate check passes: `dotnet test --filter "Category=Integration"`
- [ ] Test count: 3+ new integration tests pass (get-by-recipient with multiple results, update-status, update-status-preserves-other-attributes)

**Tests**: integration
**Gate**: full

**Verify**: `dotnet test --filter "Category=Integration"` → all `DynamoDbNotificationRepository` tests pass, 0 failures

**Commit**: `feat(infrastructure): add DynamoDbNotificationRepository recipient query and status update`

---

### T05: `DynamoDbConsentRepository` [P]

**What**: `FindAsync(recipientId, channel)` via direct `GetItemAsync` on `PK=CONSENT#{recipientId}`, `SK=CHANNEL#{channel}`
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/DynamoDbConsentRepository.cs` (new file)
**Depends on**: T01, T02
**Reuses**: same primary-constructor shape as `DynamoDbNotificationRepository`
**Requirement**: SESDB-03

**Tools**:
- MCP: none (same `GetItemAsync` API already confirmed via T03)
- Skill: none

**Done when**:
- [ ] `FindAsync` with no matching item → `null`
- [ ] `FindAsync` with a seeded consent item → fully-hydrated `ConsentPreference` (`OptedIn`, `UpdatedAt` correct)
- [ ] `Channel` persisted/read as its string name
- [ ] Gate check passes: `dotnet test --filter "Category=Integration"`
- [ ] Test count: 3+ integration tests pass

**Tests**: integration
**Gate**: full

**Verify**: `dotnet test --filter "Category=Integration"` → all `DynamoDbConsentRepository` tests pass, 0 failures

**Commit**: `feat(infrastructure): add DynamoDbConsentRepository`

---

### T06: `ScribanTemplateRenderer` + embedded template [P]

**What**: Renders a named template (loaded from an embedded resource) against a payload dictionary; validates required fields
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Templates/ScribanTemplateRenderer.cs`, `Templates/Files/welcome-email.scriban` (marked `EmbeddedResource` in the `.csproj`)
**Depends on**: T01
**Reuses**: none — first template renderer in this codebase
**Requirement**: SESDB-04, SESDB-05

**Tools**:
- MCP: context7 (Scriban API — `Template.Parse`/`Render` exact usage, and how to detect unresolved/missing variables so a missing payload field surfaces as an error rather than rendering blank)
- Skill: none

**Done when**:
- [ ] `RenderAsync("welcome-email", payload)` with a complete payload (`{"name": "Alice"}`) → exact expected rendered string
- [ ] `RenderAsync("unknown-template", payload)` → `ErrorOr` not-found error, no exception
- [ ] `RenderAsync("welcome-email", incompletePayload)` (missing `name`) → `ErrorOr` validation error, not a blank substitution
- [ ] Scriban runs in its default (non-`ScriptObject`-elevated) mode — no file/network access exposed to template scripts
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 3+ unit tests pass

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ScribanTemplateRenderer` tests pass, 0 failures

**Commit**: `feat(infrastructure): add ScribanTemplateRenderer with welcome-email template`

---

### T07: `SesEmailSender`

**What**: Implements `IEmailSender` via `IAmazonSimpleEmailService.SendEmailAsync`, using the verified sender ARN from `ISecretsProvider`
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Email/SesEmailSender.cs`
**Depends on**: T01, T02
**Reuses**: `ISecretsProvider` (E-01)
**Requirement**: SESDB-06, SESDB-07

**Tools**:
- MCP: context7 (AWSSDK.SimpleEmail `SendEmailAsync` request shape — `Destination`, `Message`, `Source`)
- Skill: none

**Done when**:
- [ ] `SendAsync` succeeds against LocalStack SES → `Result.Success`
- [ ] A simulated SES failure (LocalStack doesn't easily simulate throttling — use a mocked `IAmazonSimpleEmailService` throwing `AmazonSimpleEmailServiceException` for this specific case) → `ErrorOr` failure, not thrown
- [ ] Gate check passes: `dotnet test --filter "Category=Integration"` for the LocalStack-backed success case; `dotnet test --filter "Category!=Integration"` for the mocked-failure case (unit test, no LocalStack needed)
- [ ] Test count: 2+ tests pass (1 integration, 1 unit)

**Tests**: integration (success path) + unit (failure path, mocked client)
**Gate**: full

**Verify**: `dotnet test` → all `SesEmailSender` tests pass, 0 failures

**Commit**: `feat(infrastructure): add SesEmailSender`

---

### T08: `MockEmailSender` [P]

**What**: No-op `IEmailSender` recording calls for local/test use
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Email/MockEmailSender.cs`
**Depends on**: None
**Reuses**: none
**Requirement**: SESDB-07

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `SendAsync` records `(recipient, renderedContent)` in an accessible list and returns `Result.Success` — no network call
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 1+ unit test passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → `MockEmailSender` test passes

**Commit**: `feat(infrastructure): add MockEmailSender`

---

### T09: IoC registration

**What**: Register all four adapters plus the two new AWS clients
**Where**: `02-src/04-IoC/RentifyxCommunications.IoC/InfrastructureDependencyInjection.cs` (modify — add `AddNotificationInfrastructure`)
**Depends on**: T04, T05, T06, T07, T08
**Reuses**: existing `AddAwsOptions` fail-fast credential check (covers new clients automatically)
**Requirement**: SESDB-08

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `services.AddAWSService<IAmazonDynamoDB>()` and `services.AddAWSService<IAmazonSimpleEmailService>()` added
- [ ] `services.AddScoped<INotificationRepository, DynamoDbNotificationRepository>()`
- [ ] `services.AddScoped<IConsentRepository, DynamoDbConsentRepository>()`
- [ ] `services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>()`
- [ ] `services.AddScoped<IEmailSender, SesEmailSender>()` (production); `MockEmailSender` available for non-production environments per design (exact environment-gating mechanism finalized in this task — e.g. `IHostEnvironment.IsProduction()` check)
- [ ] `dotnet build --no-incremental` passes
- [ ] `dotnet run --project AppHost` boots without a DI resolution error (manual verification, matching SESDB-US-05's independent test)

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`; manual `dotnet run --project AppHost` boot check

**Commit**: `feat(ioc): register DynamoDB, SES, and Scriban adapters`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  ├── T01 [P]  Package references
  ├── T02 [P]  LocalStack fixture
  ├── T06 [P]  ScribanTemplateRenderer
  └── T08 [P]  MockEmailSender

Phase 2 (T03→T04 sequential; T05 parallel to that chain):
  ├── T03 ──→ T04
  └── T05 [P]

Phase 3 (after Foundation):
  T07

Phase 4 (Sequential, after everything):
  T09
```

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T01: Package references | 2 files, 1 concept | ✅ Granular |
| T02: LocalStack fixture | 1 fixture class | ✅ Granular |
| T03: Save + GetById slice | 1 file, 2 methods (cohesive: both are the "primary key" operations) | ✅ Granular |
| T04: Recipient query + status update | 1 file, 2 methods (cohesive: both are the "secondary access pattern" operations) | ✅ Granular |
| T05: DynamoDbConsentRepository | 1 class, 1 method | ✅ Granular |
| T06: ScribanTemplateRenderer | 1 class + 1 template file | ✅ Granular |
| T07: SesEmailSender | 1 class | ✅ Granular |
| T08: MockEmailSender | 1 class | ✅ Granular |
| T09: IoC registration | 1 file, 1 method | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T01 | None | Phase 1, no incoming arrow | ✅ Match |
| T02 | T01 | T01 → T02 | ✅ Match |
| T03 | T01, T02 | T01, T02 → T03 | ✅ Match |
| T04 | T03 | T03 → T04 | ✅ Match |
| T05 | T01, T02 | T01, T02 → T05 | ✅ Match |
| T06 | T01 | Phase 1 (parallel, T01 dep implicit within same phase) | ✅ Match |
| T07 | T01, T02 | Phase 3, after Foundation | ✅ Match |
| T08 | None | Phase 1, no incoming arrow | ✅ Match |
| T09 | T04, T05, T06, T07, T08 | Phase 4, after all | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T01 | Package refs | none | none | ✅ OK |
| T02 | Test fixture | none | none | ✅ OK |
| T03 | Repository methods | integration | integration | ✅ OK |
| T04 | Repository methods | integration | integration | ✅ OK |
| T05 | Repository | integration | integration | ✅ OK |
| T06 | Template renderer | unit | unit | ✅ OK |
| T07 | Email sender | integration + unit (highest: integration) | integration + unit | ✅ OK |
| T08 | Email sender (mock) | unit | unit | ✅ OK |
| T09 | IoC registration | none | none | ✅ OK |

---

## Requirement Traceability Update

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| SESDB-01 | P1: SESDB-US-01 | Tasks | Mapped → T03, T04 |
| SESDB-02 | P1: SESDB-US-01 | Tasks | Mapped → T03 |
| SESDB-03 | P1: SESDB-US-02 | Tasks | Mapped → T02, T05 |
| SESDB-04 | P1: SESDB-US-03 | Tasks | Mapped → T06 |
| SESDB-05 | P1: SESDB-US-03 | Tasks | Mapped → T06 |
| SESDB-06 | P1: SESDB-US-04 | Tasks | Mapped → T02, T07 |
| SESDB-07 | P1: SESDB-US-04 | Tasks | Mapped → T07, T08 |
| SESDB-08 | P2: SESDB-US-05 | Tasks | Mapped → T09 |

**Coverage:** 8 total, 8 mapped to tasks, 0 unmapped ✅
