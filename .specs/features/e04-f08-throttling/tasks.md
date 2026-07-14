# E-04 · F-08 — Throttling & Circuit Breaking Tasks

**Design**: `.specs/features/e04-f08-throttling/design.md`
**Status**: Approved

---

## Test Strategy (reusing gate commands from E-01/E-03/F-07)

| Gate | Command | When |
| --- | --- | --- |
| **build** | `dotnet build --no-incremental` | Config records, DI wiring — no branching logic to unit test |
| **quick** | `dotnet test --filter "Category!=Integration"` | Unit tests only (`ResiliencePipelineFactory`, `ResilientEmailSender`, `ResilienceStartupValidator`) |
| **full** | `dotnet test` | Unit + integration (none new for F-08 — no external system to integration-test against; SES/DynamoDB integration stays F-07's concern) |

## Test Coverage Matrix

| Code Layer | Test Type Required | Parallel-Safe |
| --- | --- | --- |
| NuGet package references (T01) | none | Yes |
| `ResilienceOptions` record (T02) | none (no logic) | Yes |
| `ResiliencePipelineFactory` (T03) | unit | Yes |
| `ResilientEmailSender` (T04) | unit | Yes |
| `ResilienceStartupValidator` (T05) | unit | Yes |
| IoC registration (T06) | none (build-only, exercised transitively by re-running E-03/F-07's existing handler/DI tests) | Yes |
| Burst load test (T07) | none (manual/on-demand per spec THR-08, not part of default CI gate) | Yes |
| `docs/architecture/overview.md` update (T08) | none | Yes |

**Parallelism Assessment**: All unit tests here are pure (Moq/fakes, no shared state, no Testcontainers) — parallel-safe, unlike F-07's LocalStack-backed integration tests.

---

## Execution Plan

```
Phase 1 — Foundation (Parallel):
  T01 [P]  NuGet packages (Polly.Core, Polly.RateLimiting)
  T02 [P]  ResilienceOptions config record

Phase 2 — Core logic (Parallel, after T01):
  T01 ──→ T03 [P]  ResiliencePipelineFactory + tests
  T01, T02 ──→ T05 [P]  ResilienceStartupValidator + tests

Phase 3 — Decorator (Sequential, needs the pipeline shape from T03):
  T03 ──→ T04  ResilientEmailSender + tests

Phase 4 — Wiring (Sequential):
  T02, T03, T04, T05 ──→ T06  InfrastructureDependencyInjection wiring

Phase 5 — Evidence & Docs (Parallel, after T06):
  T06 ──→ T07 [P]  Burst load test
  T06 ──→ T08 [P]  Update docs/architecture/overview.md
```

---

## Task Breakdown

### T01: Add NuGet packages for Polly [P]

**What**: Add `Polly.Core` and `Polly.RateLimiting` package references (central version management)
**Where**: `Directory.Packages.props` (new entries, `Infrastructure` `ItemGroup` or a new `ItemGroup Label="Resilience"`); `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/RentifyxCommunications.Infrastructure.csproj` (add the two `PackageReference` elements, no version attribute per central management)
**Depends on**: None
**Reuses**: F-07's `AWSSDK.DynamoDBv2`/`Scriban` entries as the version-pinning precedent
**Requirement**: THR-01 through THR-08 (foundational)

**Tools**:
- MCP: context7 (confirm latest stable `Polly.Core`/`Polly.RateLimiting` versions before pinning)
- Skill: none

**Done when**:
- [x] `Directory.Packages.props` has `PackageVersion` entries for both packages (`Polly.Core` 8.7.0, `Polly.RateLimiting` 8.7.0 — confirmed latest stable via web search 2026-07-14)
- [x] `RentifyxCommunications.Infrastructure.csproj` references both (no explicit version)
- [x] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `chore(infrastructure): add Polly.Core and Polly.RateLimiting packages`

---

### T02: `ResilienceOptions` config record [P]

**What**: Options record binding token-bucket and circuit-breaker thresholds from configuration
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Options/ResilienceOptions.cs`
**Depends on**: None
**Reuses**: `SecretsProviderOptions`'s plain-record-for-config-binding style (E-01) — corrected from design's `DynamoDbOptions` reference, which doesn't exist in PR #7's actual implementation (it hardcoded `TableName` as a const instead of an options record)
**Requirement**: THR-07

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [x] `ResilienceOptions(int TokenBucketPermitsPerSecond = 14, int TokenBucketQueueMaxWaitSeconds = 5, int CircuitBreakerMinimumThroughput = 5, int CircuitBreakerSamplingDurationSeconds = 30, int CircuitBreakerBreakDurationSeconds = 30)` record defined, matching the spec's placeholder defaults exactly
- [x] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(infrastructure): add ResilienceOptions config record`

---

### T03: `ResiliencePipelineFactory` [P]

**What**: Static factory building the `ResiliencePipeline<ErrorOr<Success>>` (token-bucket rate limiter + ratio-based circuit breaker) from `ResilienceOptions`
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Resilience/ResiliencePipelineFactory.cs`
**Depends on**: T01
**Reuses**: `ErrorOr` conventions already used throughout Application/Domain/Infrastructure
**Requirement**: THR-01, THR-02, THR-04, THR-06, THR-07

**Tools**:
- MCP: context7 (`Polly.RateLimiterStrategyOptions`/`TokenBucketRateLimiter`/`CircuitBreakerStrategyOptions` API shape — already confirmed during Design, re-verify exact constructor signatures at implementation time)
- Skill: none

**Done when**:
- [x] `Create(ResilienceOptions options): ResiliencePipeline<ErrorOr<Success>>` implemented per design: `AddCircuitBreaker` (outer) with `FailureRatio = 1.0`, `MinimumThroughput = options.CircuitBreakerMinimumThroughput`, `SamplingDuration`/`BreakDuration` from `options`, then `AddRateLimiter` (inner) wrapping a `TokenBucketRateLimiter` (`TokenLimit`/`TokensPerPeriod` derived from `TokenBucketPermitsPerSecond`, `ReplenishmentPeriod = 1s`, `QueueLimit = TokenBucketPermitsPerSecond * TokenBucketQueueMaxWaitSeconds` as the queue-depth approximation of "max wait seconds", documented as such since the BCL rate limiter doesn't expose a literal wall-clock timeout)
- [x] Circuit breaker's `ShouldHandle` is `new PredicateBuilder<ErrorOr<Success>>().HandleResult(r => r.IsError)` — the pipeline reacts to `ErrorOr` failures, not thrown exceptions (per design's "inner sender never throws" finding). The rate limiter passed via `.AddRateLimiter(RateLimiter)` doesn't need its own `ShouldHandle` — admission control isn't result-dependent.
- [x] Unit tests: (a) `TokenBucketPermitsPerSecond=1, QueueMaxWaitSeconds=0` — first call succeeds, immediate second call throws `RateLimiterRejectedException`; (b) `CircuitBreakerMinimumThroughput=2` — 2 failing calls open the circuit, the 3rd call throws `BrokenCircuitException` and never reaches the wrapped delegate (asserted via a call counter)
- [x] `dotnet test --filter "Category!=Integration"` passes (2 new tests, 0 regressions across the full unit suite)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ResiliencePipelineFactory` tests pass (2+ new tests: rate-limit enforcement, circuit-breaker open-after-threshold)

**Commit**: `feat(infrastructure): add ResiliencePipelineFactory`

---

### T04: `ResilientEmailSender`

**What**: `IEmailSender` decorator executing an inner sender's `SendAsync` through an injected `ResiliencePipeline<ErrorOr<Success>>`
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Email/ResilientEmailSender.cs`
**Depends on**: T03
**Reuses**: `IEmailSender` (E-02 Domain contract, unchanged), `ErrorOr`/`Error.Failure` conventions from `SesEmailSender` (F-07)
**Requirement**: THR-01, THR-02, THR-03, THR-04, THR-05

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [x] `SendAsync(EmailAddress recipient, string renderedContent, CancellationToken ct): Task<ErrorOr<Success>>` implemented: executes `pipeline.ExecuteAsync(async ct => await inner.SendAsync(recipient, renderedContent, ct), ct)`
- [x] Catches `RateLimiterRejectedException` and maps to `Error.Failure(ResilienceErrorCodes.RateLimitExceeded, ...)` (queue-timeout case, spec THR-03)
- [x] Catches `BrokenCircuitException` and maps to `Error.Failure(ResilienceErrorCodes.CircuitOpen, ...)` (open-circuit case, spec THR-04/THR-05) — error codes added to `Domain/Constants/ResilienceErrorCodes.cs` per CLAUDE.md's no-magic-strings rule
- [x] An inner `SendAsync` that returns `IsError = true` (no exception) propagates that same `ErrorOr` error unchanged — F-08 doesn't alter what the caller sees for an ordinary SES failure, it only adds rate-limiter/circuit-breaker accounting around it (per design's Error Handling Strategy)
- [x] Unit tests: successful inner send passes through unchanged; a fake inner sender returning errors enough times opens the circuit (via a real `ResiliencePipelineFactory.Create`-built pipeline with test-sized thresholds) and the next call is rejected without invoking the inner sender again (verified via `Moq.Times.Exactly`); a rate-limiter rejection is mapped to `Error.Failure`, not an unhandled exception
- [x] `dotnet test --filter "Category!=Integration"` passes (3 new tests, 0 regressions)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ResilientEmailSender` tests pass (4+ new tests)

**Commit**: `feat(infrastructure): add ResilientEmailSender`

---

### T05: `ResilienceStartupValidator` [P]

**What**: Fail-fast startup check that all `ResilienceOptions` values are positive
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Resilience/ResilienceStartupValidator.cs`
**Depends on**: T01, T02
**Reuses**: `SecretsStartupValidator`'s exact fail-fast-at-startup pattern (E-01) — read that class first to match its actual shape (hosted service vs. plain invoked-once class) rather than guessing, per design's flagged open question
**Requirement**: Edge case from spec ("zero/negative config must fail fast")

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [x] Validates every `ResilienceOptions` field that must be `> 0` (`TokenBucketPermitsPerSecond`, `CircuitBreakerMinimumThroughput`, `CircuitBreakerSamplingDurationSeconds`, `CircuitBreakerBreakDurationSeconds`); `TokenBucketQueueMaxWaitSeconds` is validated `>= 0` instead — `0` is a legitimate "reject immediately, never queue" setting (already used as such in T03's own tests), not a misconfiguration, so requiring it `> 0` would have been wrong (caught before committing). Throws `InvalidOperationException` naming the offending setting, mirroring `SecretsStartupValidator`'s error message style.
- [x] Confirmed `SecretsStartupValidator`'s actual shape by reading it directly: a plain class (not `IHostedService`), registered `AddSingleton<T>()`, resolved manually in `Program.cs` inside a startup scope, with an async `ValidateAsync()` called before `app.MapDefaultEndpoints()`. `ResilienceStartupValidator` mirrors the class/registration shape but exposes a synchronous `Validate()` — no I/O here (pure in-memory config check), so an `Async` suffix would be misleading per CLAUDE.md's async-naming rule. The actual DI registration + `Program.cs` invocation is T06's job (wiring), not duplicated here.
- [x] Unit tests: valid options pass without throwing; `TokenBucketQueueMaxWaitSeconds = 0` explicitly passes without throwing; a zero or negative value on each of the four required-positive fields throws identifying that field; a negative `TokenBucketQueueMaxWaitSeconds` throws too (8 tests total)
- [x] `dotnet test --filter "Category!=Integration"` passes (0 regressions)

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ResilienceStartupValidator` tests pass (2+ new tests)

**Commit**: `feat(infrastructure): add ResilienceStartupValidator`

---

### T06: Wire everything into `InfrastructureDependencyInjection`

**What**: Bind `ResilienceOptions`, register the `Singleton` pipeline via `ResiliencePipelineFactory`, register `ResilienceStartupValidator`, and wrap F-07's existing Ses/Mock `IEmailSender` factory result in `ResilientEmailSender`
**Where**: `02-src/04-IoC/RentifyxCommunications.IoC/InfrastructureDependencyInjection.cs` (extend `AddNotificationInfrastructure`)
**Depends on**: T02, T03, T04, T05
**Reuses**: The exact `IHostEnvironment`-gated `IEmailSender` factory lambda already in place for F-07 — only its final `return` changes
**Requirement**: THR-01 through THR-08 (wiring)

**Tools**:
- MCP: none (pattern already established in the codebase)
- Skill: none

**Done when**:
- [ ] `ResilienceOptions` bound from configuration and registered
- [ ] `services.AddSingleton(sp => ResiliencePipelineFactory.Create(sp.GetRequiredService<ResilienceOptions>()))` added
- [ ] `ResilienceStartupValidator` registered using the same mechanism as `SecretsStartupValidator` (per T05)
- [ ] The existing `IEmailSender` factory lambda's final `return` (Ses or Mock) is wrapped: `return new ResilientEmailSender(innerSender, sp.GetRequiredService<ResiliencePipeline<ErrorOr<Success>>>());`
- [ ] `dotnet build --no-incremental` passes
- [ ] Re-running E-03/F-07's existing handler tests (which use their own mocked `IEmailSender`, unaffected by this DI change) still pass — confirms no regression

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`; `dotnet test --filter "Category!=Integration"` → all existing + new unit tests still pass, 0 regressions

**Commit**: `feat(ioc): wire ResilientEmailSender into DI`

---

### T07: Burst load test evidence [P]

**What**: On-demand test firing 1,000 notifications in a burst against `MockEmailSender` (configured to simulate exceeding the configured rate) through the full resilience pipeline, asserting the simulated call rate never exceeds the configured token-bucket rate
**Where**: `03-tests/04-Repositories/RentifyxCommunications.Tests.Repositories/Features/Notifications/ResilientEmailSenderLoadTests.cs` (or a dedicated location if the load-test's runtime makes it unsuitable for the default `quick`/`full` gates — decide during implementation based on actual run time)
**Depends on**: T06
**Reuses**: `MockEmailSender` (F-07)
**Requirement**: THR-08

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Test fires 1,000 sends through a `ResilientEmailSender` wrapping a rate-simulating `MockEmailSender`
- [ ] Assertion: the observed call rate to the inner sender never exceeds the configured `TokenBucketPermitsPerSecond`
- [ ] Tagged so it does NOT run as part of the default CI gate (e.g. `[Trait("Category", "LoadTest")]`, excluded from the standard `--filter "Category!=Integration"` quick gate) — this is on-demand evidence per spec THR-08's P3 priority, not a required CI check
- [ ] Documented in this task how to run it manually (exact `dotnet test --filter` invocation)

**Tests**: none (per Test Coverage Matrix — this is evidence, not a CI-gated correctness test; T03/T04's unit tests already prove the mechanism)
**Gate**: n/a (manual/on-demand)

**Verify**: Manual run of the documented `dotnet test --filter` command → passes, observed rate stays at or below the configured limit

**Commit**: `test(infrastructure): add ResilientEmailSender burst load test`

---

### T08: Update `docs/architecture/overview.md`

**What**: Document the resilience layer (rate limiter + circuit breaker) in front of `IEmailSender`, its configuration keys, and the F-08 addition to the target architecture diagram
**Where**: `docs/architecture/overview.md`
**Depends on**: T06
**Reuses**: n/a — doc-only

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Diagram/prose reflects `ResilientEmailSender` sitting between `DispatchNotificationHandler` and the real `SesEmailSender`/`MockEmailSender`
- [ ] New configuration keys (`ResilienceOptions`'s five settings) documented alongside existing config docs, explicitly flagged as placeholder values pending B-001
- [ ] Notes B-001 is still open and these values should be revisited once the real SES quota is confirmed

**Tests**: none
**Gate**: n/a (documentation)

**Verify**: Manual review — content matches T02/T03/T06's actual implementation

**Commit**: `docs(architecture): update overview for F-08 resilience layer`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  T01 [P] ──┐
  T02 [P] ──┘  (independent)

Phase 2 (Parallel, after T01/T02):
  T01 complete, then:
    └── T03 [P]
  T01 + T02 complete, then:
    └── T05 [P]

Phase 3 (Sequential — needs T03's pipeline shape):
  T03 ──→ T04

Phase 4 (Sequential):
  T02, T03, T04, T05 ──→ T06

Phase 5 (Parallel, after T06):
  T06 complete, then:
    ├── T07 [P]
    └── T08 [P]
```

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T01: NuGet packages | 2 files, no logic | ✅ Granular |
| T02: ResilienceOptions | 1 record | ✅ Granular |
| T03: ResiliencePipelineFactory | 1 static class | ✅ Granular |
| T04: ResilientEmailSender | 1 class | ✅ Granular |
| T05: ResilienceStartupValidator | 1 class | ✅ Granular |
| T06: IoC wiring | 1 file (extended) | ✅ Granular |
| T07: Burst load test | 1 test file | ✅ Granular |
| T08: Docs update | 1 file | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T01 | None | No incoming arrow | ✅ Match |
| T02 | None | No incoming arrow | ✅ Match |
| T03 | T01 | T01 → T03 | ✅ Match |
| T04 | T03 | T03 → T04 | ✅ Match |
| T05 | T01, T02 | T01 → T05, T02 → T05 | ✅ Match |
| T06 | T02, T03, T04, T05 | All four → T06 | ✅ Match |
| T07 | T06 | T06 → T07 | ✅ Match |
| T08 | T06 | T06 → T08 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T01 | NuGet package refs | none | none | ✅ OK |
| T02 | `ResilienceOptions` | none | none | ✅ OK |
| T03 | `ResiliencePipelineFactory` | unit | unit | ✅ OK |
| T04 | `ResilientEmailSender` | unit | unit | ✅ OK |
| T05 | `ResilienceStartupValidator` | unit | unit | ✅ OK |
| T06 | IoC registration | none | none | ✅ OK |
| T07 | Load test | none (evidence, not CI-gated) | none | ✅ OK |
| T08 | Docs | none | none | ✅ OK |

All three pre-approval checks pass — no restructuring needed.

---

## Requirement Coverage

All 8 `THR-*` requirements from `spec.md` map to at least one task above (T03/T04 carry the bulk; T01, T02, T05, T06, T08 are foundational/wiring/docs; T07 covers THR-08 directly). No unmapped requirements.
