# E-05 F-10: Status & Consent Endpoints Tasks

**Design**: `.specs/features/e05-f10-status-consent-endpoints/design.md`
**Status**: Draft

---

## Test Coverage Matrix (derived from codebase — no `.specs/codebase/TESTING.md` exists yet)

Derived directly from `03-tests/` project structure and existing test files (not fabricated):

| Code Layer | Test Type | Test Project | Gate | Parallel-Safe |
|---|---|---|---|---|
| Domain value object / interface (no logic) | none | — | build | Yes |
| Application handler (`IHandler<TRequest,TResponse>`) | unit | `03-Handlers` | quick (`dotnet test --filter "FullyQualifiedName~Tests.Handlers"`) | Yes |
| FluentValidation validator | unit | `02-Validators` or co-located in `03-Handlers` (existing precedent: validators live beside their handler's test, no dedicated `02-Validators` entries found for recent features) | quick | Yes |
| DynamoDB repository (new methods on existing repo, or new repo class) | integration only (LocalStack Testcontainers) — confirmed no `04-Repositories` unit tests exist for any `DynamoDb*Repository`, only `05-Integration` | `05-Integration` | full (`dotnet test --filter "Category=Integration"`) | **No** (shared LocalStack container/table) |
| Api middleware / auth handler (`DefaultHttpContext`-style, no TestServer) | unit | `06-Api` | quick | Yes |
| `IEndpoint` thin wrapper (route → handler) | none — confirmed no dedicated endpoint-level test files exist for any prior feature (`DispatchNotification` endpoint has none either); covered transitively by the handler's unit test + the auth/e2e integration test below | — | build | Yes |
| End-to-end HTTP behavior (401, audit read-back) | integration | `05-Integration` (new) | full (`Category=Integration`) | **No** |

**Gate check commands** (from `.github/workflows/ci.yml`):
- Quick: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- Full: `dotnet test RentifyxCommunications.slnx --filter "Category=Integration"`
- Build: `dotnet build RentifyxCommunications.slnx`

---

## Execution Plan

### Phase 1: Domain Foundations (Parallel)

```
T1 [P] ─┐
T2 [P] ─┼──→ (Phase 2)
T3 [P] ─┘
```

### Phase 2: Infrastructure (Parallel, depends on Phase 1)

```
T1,T2 ──→ T4 [P] ─┐
T3      ──→ T5 [P] ─┼──→ (Phase 3)
```

### Phase 3: Application Handlers (Parallel, depends on Phase 1/2 interfaces)

```
        T6 [P] ─┐
        T7 [P] ─┤
T3   ──→ T8 [P] ─┼──→ (Phase 5)
T2,T3 ──→ T9 [P] ─┘
```

### Phase 4: Api Cross-Cutting (Parallel, independent of Phase 3)

```
T10 [P] ─┐
T11 [P] ─┼──→ (Phase 5)
T12 [P] ─┘
```

### Phase 5: Endpoints (Sequential — shared `Program.cs`/route-group wiring)

```
T6,T7,T10       ──→ T13
T8,T9,T10,T12   ──→ T14
```

### Phase 6: Docs & E2E Verification (Sequential)

```
T13,T14 ──→ T15 ──→ T16
```

---

## Task Breakdown

### T1: Create `ConsentAuditEntry` value object [P]

**What**: New immutable record capturing one consent-change audit fact.
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/ValueObjects/ConsentAuditEntry.cs`
**Depends on**: None
**Reuses**: `Channel` enum (existing)
**Requirement**: API-04

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Record defined exactly as in design.md (`RecipientId`, `Channel`, `PreviousOptedIn`, `NewOptedIn`, `ChangedAt`), multi-line params per CLAUDE.md
- [ ] No behavior/validation (plain data carrier — matches `RetryContext` precedent, not an entity)
- [ ] Gate check passes: `dotnet build RentifyxCommunications.slnx`

**Tests**: none
**Gate**: build

---

### T2: Create `IConsentAuditRepository` interface [P]

**What**: New interface with a single `AddAsync` method.
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/Notifications/IConsentAuditRepository.cs`
**Depends on**: None
**Reuses**: `Notifications/` interface segmentation folder (existing, houses `INotificationRepository`/`IConsentRepository`)
**Requirement**: API-04

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `Task AddAsync(ConsentAuditEntry entry, CancellationToken cancellationToken = default)` declared, `Async` suffix per CLAUDE.md
- [ ] Gate check passes: `dotnet build RentifyxCommunications.slnx`

**Tests**: none
**Gate**: build

---

### T3: Rename `IConsentRepository.FindAsync` → `GetAsync`, add `UpdateAsync`, fix call sites [P]

**What**: Interface method rename + new method signature; update every caller.
**Where**: `Domain/Interfaces/Notifications/IConsentRepository.cs` (modify); call sites in `Application/Features/Notifications/` (`ConsentEnforcement`, `DispatchNotificationHandler`) and their existing `03-Handlers` tests
**Depends on**: None
**Reuses**: Existing `ConsentPreference` value object, `ConsentDecision.NoRecordFound()`/`FromPreference()` domain factories (AD-013/AD-014 — unchanged)
**Requirement**: API-03, API-04

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `FindAsync` renamed to `GetAsync` everywhere (interface + all implementations + all call sites + all existing test mocks/stubs)
- [ ] `Task UpdateAsync(ConsentPreference consent, CancellationToken cancellationToken = default)` added to the interface (implementation lands in T5, this task is interface + call-site rename only)
- [ ] All pre-existing `03-Handlers` tests referencing `FindAsync` updated and still passing
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: existing handler test count unchanged, 0 failures (no silent deletions)

**Tests**: unit (existing suite, updated in place)
**Gate**: quick

---

### T4: `DynamoDbConsentAuditRepository` + `ConsentAuditItemMapper` [P]

**What**: New Infrastructure repository implementing `IConsentAuditRepository`, writing `PK=CONSENT#{recipientId}` / `SK=AUDIT#{channel}#{changedAt:O}` items.
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/Notifications/DynamoDbConsentAuditRepository.cs`, `.../ConsentAuditItemMapper.cs`
**Depends on**: T1, T2
**Reuses**: `NotificationTableSchema` constants, `DynamoDbOptions.NotificationsTableName`, `IAmazonDynamoDB` DI registration (all existing, unchanged) — same shape as `DynamoDbConsentRepository`/`ConsentItemMapper`

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Enum persisted as string (`Channel.ToString()`), per CLAUDE.md Enum Persistence rule
- [ ] Registered in `InfrastructureDependencyInjection`
- [ ] New `05-Integration` test class `DynamoDbConsentAuditRepositoryTests.cs` (LocalStack Testcontainers, `Category=Integration`) verifying: write succeeds, a `Query` on `PK=CONSENT#{recipientId}`/`SK begins_with "AUDIT#"` returns the item, `PreviousOptedIn=null` case (first-ever change) round-trips correctly
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category=Integration"`
- [ ] Test count: 3+ new integration tests pass

**Tests**: integration
**Gate**: full

---

### T5: `DynamoDbConsentRepository.UpdateAsync` + rename `FindAsync`→`GetAsync` (implementation) [P]

**What**: Implement the new interface method from T3 on the concrete repository; rename existing method.
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/Notifications/DynamoDbConsentRepository.cs` (modify)
**Depends on**: T3
**Reuses**: `ConsentItemMapper`, existing `CONSENT#{recipientId}`/`CHANNEL#{channel}` key scheme (no schema change — `PutItem` upsert, no `ConditionExpression` needed per spec's "last-write-wins" edge case)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `UpdateAsync` does a plain `PutItem` (create-or-update, no condition) matching spec's documented last-write-wins limitation
- [ ] Existing `DynamoDbConsentRepositoryTests.cs` (`05-Integration`) updated for the rename, plus new cases for `UpdateAsync` (create when absent, overwrite when present)
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category=Integration"`
- [ ] Test count: existing tests renamed/passing + 2+ new `UpdateAsync` tests pass

**Tests**: integration
**Gate**: full

---

### T6: `GetNotificationStatusHandler` (+ Request/Response/Validator) [P]

**What**: Application handler wrapping `INotificationRepository.GetByIdAsync`, mapping entity → response DTO.
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/GetStatus/GetNotificationStatusHandler.cs` (+ `Request/GetNotificationStatusRequest.cs`, `Response/NotificationStatusResponse.cs`, `Validator/GetNotificationStatusValidator.cs`)
**Depends on**: None (uses existing `INotificationRepository`)
**Reuses**: `IHandler<TRequest,TResponse>`, `DispatchNotificationHandler`'s primary-constructor DI style
**Requirement**: API-01

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `Request` carries `id` as `string` (not `Guid`) — route constraint decision below (T13) means validation happens here, not at routing
- [ ] `Validator` rejects a non-parseable GUID with a 400-mapping validation error (resolves design's open "404-vs-400" item: route accepts `{id}` unconstrained, validator enforces format, `ToProblem`'s `Validation` branch already maps to 400)
- [ ] `Error.NotFound` returned when `GetByIdAsync` misses
- [ ] Response includes id, correlationId, recipientId, channel, status, failureReason, createdAt, updatedAt (spec AC1)
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: 4+ new unit tests pass (found, not-found, invalid-guid-validation, response-mapping)

**Tests**: unit
**Gate**: quick

---

### T7: `GetNotificationsByRecipientHandler` (+ Request/Response/Validator) [P]

**What**: Application handler wrapping `INotificationRepository.GetByRecipientAsync`.
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/GetByRecipient/` (`GetNotificationsByRecipientHandler.cs`, `Request/`, `Response/`, `Validator/`)
**Depends on**: None
**Reuses**: `IHandler<TRequest,TResponse>`, same pattern as T6
**Requirement**: API-02

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Empty list returned as `200` success (not `Error.NotFound`) per spec AC2
- [ ] Same GUID-validation-via-Validator approach as T6 for `recipientId`
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: 3+ new unit tests pass (empty list, populated list, invalid-guid-validation)

**Tests**: unit
**Gate**: quick

---

### T8: `GetConsentHandler` (+ Request/Response/Validator) [P]

**What**: Application handler wrapping `IConsentRepository.GetAsync`, applying AD-013 default when no record exists.
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Consent/Handlers/Get/` (`GetConsentHandler.cs`, `Request/`, `Response/`, `Validator/`)
**Depends on**: T3 (needs `GetAsync`, not `FindAsync`)
**Reuses**: `ConsentDecision.NoRecordFound()` domain factory (existing, from AD-013/E-03)
**Requirement**: API-03

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] No-record case returns `optedIn: true` via the same `ConsentDecision.NoRecordFound()` call the dispatch path uses (no re-implemented default)
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: 3+ new unit tests pass (record exists, no record → default true, invalid-guid-validation)

**Tests**: unit
**Gate**: quick

---

### T9: `UpdateConsentHandler` (+ Request/Response/Validator) [P]

**What**: Application handler orchestrating consent write then audit write, in that order.
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Consent/Handlers/Update/` (`UpdateConsentHandler.cs`, `Request/`, `Response/`, `Validator/`)
**Depends on**: T2 (needs `IConsentAuditRepository`), T3 (needs `GetAsync`/`UpdateAsync` signatures)
**Reuses**: `ConsentPreference.Create(...)` factory, `IHandler<TRequest,TResponse>`
**Requirement**: API-04

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `Validator` rejects missing/malformed `optedIn` (spec AC4, 400/422 via `ToProblem`)
- [ ] Handler reads previous state via `IConsentRepository.GetAsync` before calling `UpdateAsync` (needed to populate `ConsentAuditEntry.PreviousOptedIn`)
- [ ] `UpdateAsync` called before `IConsentAuditRepository.AddAsync`, in that order (design's resolved sequencing decision)
- [ ] Audit-write failure after a successful consent write surfaces as `Error.Unexpected` (500) — documented known limitation, not swallowed
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: 4+ new unit tests pass (create-first-record, overwrite-existing, validation failure, audit-write-failure-surfaces-error)

**Tests**: unit
**Gate**: quick

---

### T10: `ApiKeyAuthenticationHandler` + `AddApiKeyAuthentication` extension + `SecretsStartupValidator` update [P]

**What**: Custom `AuthenticationHandler` scheme validating `X-Api-Key` against Secrets Manager; wires `AddAuthentication`/`UseAuthentication`/`UseAuthorization` into the pipeline; adds `ApiKey` to the required-secrets fail-fast list.
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Authentication/ApiKeyAuthenticationHandler.cs`, `Extensions/AuthenticationExtension.cs`, `Program.cs` (modify — insert `UseAuthentication()`/`UseAuthorization()` at position 9, before `MapEndpoints()`), `Infrastructure/Secrets/SecretsStartupValidator.cs` (modify)
**Depends on**: None
**Reuses**: `ISecretsProvider`/`SecretsManagerProvider`'s existing 5-min cache, `SecretsStartupValidator` fail-fast pattern
**Requirement**: API-05

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Header comparison uses `CryptographicOperations.FixedTimeEquals` (constant-time, avoids timing side-channel)
- [ ] Empty-string header treated as missing → 401 (spec edge case), not anonymous
- [ ] `HealthCheck.cs`'s existing `.AllowAnonymous()` still resolves to 200 unauthenticated (regression check — it was previously inert)
- [ ] `SecretsStartupValidator` fails fast at boot if `ApiKey` secret is missing, same pattern as `SesArn`/Kafka creds
- [ ] New `06-Api` unit tests for `ApiKeyAuthenticationHandler` (`DefaultHttpContext`-style, mocked `ISecretsProvider`, no TestServer) — matches `CorrelationIdMiddlewareTests` precedent
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: 4+ new unit tests pass (valid key, missing key, invalid key, empty-string key)

**Tests**: unit
**Gate**: quick

---

### T11: `SecurityHeadersMiddleware` + `UseSecurityHeaders` extension [P]

**What**: New middleware adding `Strict-Transport-Security`, `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy` to every response.
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Middlewares/SecurityHeadersMiddleware.cs`, `Extensions/MiddlewareExtensions.cs` (modify), `Program.cs` (modify — insert right after `UseExceptionHandler()`, before `UseCorrelationId()`)
**Depends on**: None
**Reuses**: `CorrelationIdMiddleware`'s plain-middleware-class shape
**Requirement**: API-07

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] CSP directive resolved (design's open item): starts with `default-src 'self'`
- [ ] Headers set via `context.Response.OnStarting(...)`, present on error responses too (registered before `UseCorrelationId()`)
- [ ] Headers present in Development, not gated by environment (spec AC2 — parity with prod)
- [ ] New `06-Api` unit test class (`DefaultHttpContext`-style) confirming all four headers present, including on a simulated error response
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category!=Integration&Category!=LoadTest"`
- [ ] Test count: 2+ new unit tests pass

**Tests**: unit
**Gate**: quick

---

### T12: `"consent"` rate-limit policy [P]

**What**: Second named rate-limiter policy, stricter than the default `"fixed"` policy, added inside the existing `AddRateLimiter` call.
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Extensions/RateLimitExtension.cs` (modify)
**Depends on**: None
**Reuses**: Existing `AddRateLimiting` extension method, same `IOptions`-free plain-record config pattern as `RateLimit`/`Cors`/`OpenApi` (CLAUDE.md Configuration Binding)
**Requirement**: API-06

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `ConsentPolicyName` constant added (no magic string per CLAUDE.md)
- [ ] Policy's `PermitLimit` is meaningfully lower than the default `"fixed"` policy's (exact numbers: match existing `RateLimit` config record's field naming, add a `Consent` sub-section)
- [ ] Gate check passes: `dotnet build RentifyxCommunications.slnx` (no dedicated unit test exists for `RateLimitExtension` today — verified transitively by T16's 429 integration test)

**Tests**: none
**Gate**: build

---

### T13: Notification status endpoints (`GetNotificationStatusEndpoint`, `GetNotificationsByRecipientEndpoint`)

**What**: Two `IEndpoint` HTTP wrappers mapping routes to T6/T7's handlers.
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Endpoints/Notifications/GetNotificationStatusEndpoint.cs`, `GetNotificationsByRecipientEndpoint.cs`
**Depends on**: T6, T7, T10
**Reuses**: `IEndpoint` + `AddEndpoints`/`MapEndpoints` auto-discovery, `ErrorOrExtensions.ToResult`
**Requirement**: API-01, API-02

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `GET /v1/api/notifications/{id}` (unconstrained `string`, not `{id:guid}` — validator in T6 handles format) → `.RequireAuthorization()`
- [ ] `GET /v1/api/notifications/recipient/{recipientId}` (unconstrained `string`) → `.RequireAuthorization()`
- [ ] Both auto-discovered and mapped under existing `v1` group, no new registration plumbing
- [ ] Gate check passes: `dotnet build RentifyxCommunications.slnx`

**Tests**: none (covered transitively by T6/T7 unit tests + T16 integration test)
**Gate**: build

---

### T14: Consent endpoints (`GetConsentEndpoint`, `UpdateConsentEndpoint`)

**What**: Two `IEndpoint` HTTP wrappers mapping routes to T8/T9's handlers, nested under a stricter rate-limit group.
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Endpoints/Consent/GetConsentEndpoint.cs`, `UpdateConsentEndpoint.cs`
**Depends on**: T8, T9, T10, T12
**Reuses**: `IEndpoint`, `app.MapGroup("consent").RequireRateLimiting(RateLimitExtension.ConsentPolicyName)`
**Requirement**: API-03, API-04

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `GET /v1/api/consent/{recipientId}?channel={channel}` → `.RequireAuthorization()`
- [ ] `PUT /v1/api/consent/{recipientId}` (body `{ channel, optedIn }`) → `.RequireAuthorization()`
- [ ] Both mapped inside the `consent` group overriding the parent `v1` group's `"fixed"` policy with `"consent"`
- [ ] Gate check passes: `dotnet build RentifyxCommunications.slnx`

**Tests**: none (covered transitively by T8/T9 unit tests + T16 integration test)
**Gate**: build

---

### T15: Scalar UI verification + `docs/contracts/notification-requested.md`

**What**: Confirm the 4 new endpoints appear in `/scalar` (should be automatic via `IEndpoint` auto-discovery — no new code expected), and write the hand-written Kafka contract doc referenced since AD-011.
**Where**: `docs/contracts/notification-requested.md` (new)
**Depends on**: T13, T14
**Reuses**: Existing `OpenApiExtensions.cs` (dev-only Scalar wiring, unchanged)
**Requirement**: API-08

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] `/scalar` loaded locally (dev environment), all 4 new endpoints visible with accurate request/response schemas (manual verification, screenshot or description in commit message — no automated test for UI rendering itself)
- [ ] `docs/contracts/notification-requested.md` documents the `NotificationRequested` event schema, required fields, and `Channel`'s reserved-but-unimplemented values (`Sms`, `Push` per AD-002)
- [ ] Doc's schema matches current `DispatchNotificationRequest` shape exactly (cross-checked against source, not written from memory)
- [ ] Gate check passes: `dotnet build RentifyxCommunications.slnx`

**Tests**: none
**Gate**: build

---

### T16: End-to-end integration tests — auth, audit trail, rate limiting

**What**: New `05-Integration` test class exercising all four endpoints against a real/LocalStack-backed running instance.
**Where**: `03-tests/05-Integration/RentifyxCommunications.Tests.Integration/Api/StatusConsentEndpointsTests.cs` (new)
**Depends on**: T13, T14
**Reuses**: `LocalStackNotificationInfrastructureFixture` (existing, extend if a `WebApplicationFactory`-equivalent host is needed — none currently exists post scaffold-cleanup, this task must decide/build the minimal host wiring needed)
**Requirement**: API-01 through API-07 (success criteria verification)

**Tools**: MCP: NONE / Skill: NONE

**Done when**:
- [ ] Unauthenticated request to each of the 4 endpoints returns 401 (spec Success Criteria)
- [ ] `PUT /consent` followed by a `GET` on the audit partition confirms a queryable audit trail (spec Success Criteria)
- [ ] Requests past the `"consent"` policy threshold return 429 well before the default `"fixed"` policy would (spec P2 AC)
- [ ] Gate check passes: `dotnet test RentifyxCommunications.slnx --filter "Category=Integration"`
- [ ] Test count: 4+ new integration tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): E-05 F-10 status & consent endpoints`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  T1, T2, T3

Phase 2 (Parallel, after Phase 1):
  T4 (needs T1,T2), T5 (needs T3)

Phase 3 (Parallel, after Phase 1/2 interfaces exist):
  T6, T7 (no deps) ── T8, T9 (need T2/T3)

Phase 4 (Parallel, independent of Phase 3):
  T10, T11, T12

Phase 5 (Sequential, after Phase 3+4):
  T13 (needs T6,T7,T10) ── T14 (needs T8,T9,T10,T12)

Phase 6 (Sequential, after Phase 5):
  T15 ── T16
```

---

## Task Granularity Check

| Task | Scope | Status |
|---|---|---|
| T1 | 1 value object | ✅ Granular |
| T2 | 1 interface | ✅ Granular |
| T3 | 1 interface change + call-site fixes | ✅ Granular (one cohesive rename) |
| T4 | 1 repository + 1 mapper (cohesive pair, matches existing `DynamoDbConsentRepository`/`ConsentItemMapper` precedent) | ✅ Granular |
| T5 | 1 file, 2 methods (rename + new) | ✅ Granular |
| T6 | 1 handler + Request/Response/Validator (one feature slice) | ✅ Granular |
| T7 | 1 handler + Request/Response/Validator | ✅ Granular |
| T8 | 1 handler + Request/Response/Validator | ✅ Granular |
| T9 | 1 handler + Request/Response/Validator | ✅ Granular |
| T10 | 1 auth handler + 1 extension + 1 validator update (cohesive: all needed for auth to function at all) | ✅ Granular |
| T11 | 1 middleware + 1 extension | ✅ Granular |
| T12 | 1 policy addition to 1 existing file | ✅ Granular |
| T13 | 2 endpoints, same feature area, same file pattern | ✅ Granular (2-3 related things, cohesive) |
| T14 | 2 endpoints, same feature area | ✅ Granular |
| T15 | 1 doc file + manual UI check | ✅ Granular |
| T16 | 1 test file, cohesive e2e verification | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T1 | None | Phase 1, no incoming arrow | ✅ Match |
| T2 | None | Phase 1, no incoming arrow | ✅ Match |
| T3 | None | Phase 1, no incoming arrow | ✅ Match |
| T4 | T1, T2 | Phase 2, arrow from T1,T2 | ✅ Match |
| T5 | T3 | Phase 2, arrow from T3 | ✅ Match |
| T6 | None | Phase 3, no incoming arrow | ✅ Match |
| T7 | None | Phase 3, no incoming arrow | ✅ Match |
| T8 | T3 | Phase 3, arrow from T3 | ✅ Match |
| T9 | T2, T3 | Phase 3, arrow from T2,T3 | ✅ Match |
| T10 | None | Phase 4, no incoming arrow | ✅ Match |
| T11 | None | Phase 4, no incoming arrow | ✅ Match |
| T12 | None | Phase 4, no incoming arrow | ✅ Match |
| T13 | T6, T7, T10 | Phase 5, arrow from T6,T7,T10 | ✅ Match |
| T14 | T8, T9, T10, T12 | Phase 5, arrow from T8,T9,T10,T12 | ✅ Match |
| T15 | T13, T14 | Phase 6, arrow from T13,T14 | ✅ Match |
| T16 | T13, T14 | Phase 6, arrow from T13,T14 (after T15 in sequence) | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
|---|---|---|---|---|
| T1 | Domain value object, no logic | none | none | ✅ OK |
| T2 | Domain interface | none | none | ✅ OK |
| T3 | Domain interface + Application call sites | unit (existing suite updated) | unit | ✅ OK |
| T4 | Infrastructure DynamoDB repository | integration | integration | ✅ OK |
| T5 | Infrastructure DynamoDB repository | integration | integration | ✅ OK |
| T6 | Application handler | unit | unit | ✅ OK |
| T7 | Application handler | unit | unit | ✅ OK |
| T8 | Application handler | unit | unit | ✅ OK |
| T9 | Application handler | unit | unit | ✅ OK |
| T10 | Api auth handler + middleware wiring | unit | unit | ✅ OK |
| T11 | Api middleware | unit | unit | ✅ OK |
| T12 | Api extension method (config only, no DI-constructed class) | none (no precedent found for `RateLimitExtension` tests) | none | ✅ OK |
| T13 | Api `IEndpoint` | none (no precedent for endpoint-level tests) | none | ✅ OK |
| T14 | Api `IEndpoint` | none | none | ✅ OK |
| T15 | Docs only | none | none | ✅ OK |
| T16 | Cross-cutting e2e (auth + audit + rate limit) | integration | integration | ✅ OK |

All ✅. No restructuring needed.

---

## Note on Test Coverage Matrix

No `.specs/codebase/TESTING.md` exists in this repo yet. The matrix above was derived directly from `03-tests/` project structure and existing test file conventions (confirmed via direct inspection, not assumed) rather than a documented source. Recommend running `map-codebase` or writing `TESTING.md` in a future session so this derivation doesn't have to be redone per feature.
