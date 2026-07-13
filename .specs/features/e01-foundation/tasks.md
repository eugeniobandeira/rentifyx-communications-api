# E-01 · Project Foundation & DevSecOps Pipeline — Tasks

**Spec**: `.specs/features/e01-foundation/spec.md`
**Status**: Draft

---

## Test Strategy (TESTING.md not yet created — defined inline)

| Gate | Command | When |
|---|---|---|
| **build** | `dotnet build --no-incremental` | Config/scaffold tasks with no testable logic |
| **quick** | `dotnet test --filter "Category!=Integration"` | Unit tests only |
| **full** | `dotnet test` | Unit + integration (Testcontainers) |

**Parallelism:**
- Unit tests → parallel-safe ✅
- Integration tests (Testcontainers) → NOT parallel-safe ❌ (shared container lifecycle in xUnit collection fixtures)
- Build-only → parallel-safe ✅

---

## Execution Plan

```
Phase 1 — Bootstrap (Sequential):
  T01 ──→ T02 ──→ T03

Phase 2 — Parallel Tracks (after T03):
                   ┌──→ T04 ──→ T07 ──→ T08
  T03 complete ────┼──→ T11 [P]
                   ├──→ T13 [P]
                   └──→ T17 [P]

Phase 3 — Build on Phase 2:
  T04 complete ────┬──→ T05 ──→ T06
                   └──→ T09 [P with T07, after T04]

  T13 complete ────┬──→ T14 [P]
                   └──→ T15 [P]

Phase 4 — Integration (after deps):
  T05 + T09 ──→ T10
  T05 + T11 ──→ T12
  T14 + T15 ──→ T16
```

---

## Task Breakdown

### T01: Create solution + project files — ✅ DONE (2026-07-11, via existing scaffold)

**What**: Scaffold the solution with `dotnet new` + project structure: API, Application, Domain, Infrastructure, Tests.Unit, Tests.Integration
**Where**: `/` (repo root), `01-aspire/`, `02-src/`, `03-tests/`
**Depends on**: None
**Reuses**: Existing `RentifyxCommunications.slnx` (check if already created by template; adapt rather than overwrite)
**Requirement**: E01-01

**Resolution note**: Repo already had a full Clean Architecture scaffold from a template. Verified rather than recreated. Naming differs from spec (`RentifyxCommunications.Api` vs `RentifyX.Communications.API`) and tests are split into 5 projects (`Tests.Common/Validators/Handlers/Repositories/Integration`) instead of `Tests.Unit`/`Tests.Integration` — functionally equivalent, treated as satisfying this task. `dotnet build --no-incremental` → 0 errors.

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `RentifyX.Communications.API` project exists (Minimal API)
- [ ] `RentifyX.Communications.Application` project exists (class library)
- [ ] `RentifyX.Communications.Domain` project exists (class library)
- [ ] `RentifyX.Communications.Infrastructure` project exists (class library)
- [ ] `RentifyX.Communications.Tests.Unit` project exists (xUnit)
- [ ] `RentifyX.Communications.Tests.Integration` project exists (xUnit + Testcontainers)
- [ ] All projects referenced in solution file
- [ ] `dotnet build` passes from repo root (zero errors)

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `Build succeeded. 0 Error(s)`

**Commit**: `chore: scaffold solution structure with Clean Architecture layers`

---

### T02: Configure Directory.Build.props + Directory.Packages.props — ✅ DONE (2026-07-11, already satisfied)

**Resolution note**: All 4 MSBuild props + centralized package management already present and correct in the existing files. Build produces 1 pre-existing warning (`ASPIRE004`, AppHost→ServiceDefaults project-resource wiring) — that's an Aspire AppHost concern, out of scope for this task's files (Directory.Build.props/Directory.Packages.props only); deferred to T04 where AppHost.csproj is actually touched.

**What**: Centralized MSBuild config — treat warnings as errors, nullable enable, implicit usings, LangVersion; centralized NuGet package versions
**Where**: `Directory.Build.props`, `Directory.Packages.props` (repo root)
**Depends on**: T01
**Reuses**: Pattern from `Directory.Build.props` already present in repo root (check first)
**Requirement**: E01-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` set in Directory.Build.props
- [ ] `<Nullable>enable</Nullable>` set
- [ ] `<ImplicitUsings>enable</ImplicitUsings>` set
- [ ] `<LangVersion>latest</LangVersion>` set
- [ ] `Directory.Packages.props` created with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
- [ ] `dotnet build` still passes with zero errors and zero warnings

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: `dotnet build --no-incremental` → `0 Warning(s)` in output

**Commit**: `chore: configure centralized build props and NuGet package versions`

---

### T03: Add .editorconfig with CA5xxx security analyzer rules — ✅ DONE (2026-07-11, already satisfied)

**Resolution note**: All 5 required rules (CA5350, CA5351, CA5359, CA5360, CA5394) already present as `error` in the existing `.editorconfig`, plus a much broader CA53xx security ruleset already in place. No changes needed.

**What**: Copy `.editorconfig` from repo root (already present) and ensure CA5xxx (insecure crypto) rules are set to `error` severity
**Where**: `.editorconfig` (repo root — already exists per git status)
**Depends on**: T01
**Reuses**: `.editorconfig` already present in repo (read first, extend rather than replace)
**Requirement**: E01-08

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `dotnet_diagnostic.CA5350.severity = error` (Do Not Use Weak Cryptographic Algorithms)
- [ ] `dotnet_diagnostic.CA5351.severity = error` (Do Not Use Broken Cryptographic Algorithms)
- [ ] `dotnet_diagnostic.CA5359.severity = error` (Do Not Disable Certificate Validation)
- [ ] `dotnet_diagnostic.CA5360.severity = error` (Do Not Call Dangerous Methods In Deserialization)
- [ ] `dotnet_diagnostic.CA5394.severity = error` (Do Not Use Insecure Randomness)
- [ ] `dotnet build` still passes (no existing violations)

**Tests**: none
**Gate**: build — `dotnet build --no-incremental`

**Verify**: Introduce a deliberate `new MD5CryptoServiceProvider()` in a test file → `dotnet build` should fail with CA5351; then remove it.

**Commit**: `chore: enforce CA5xxx security analyzer rules in .editorconfig`

---

### T04: Configure Aspire AppHost + ServiceDefaults — ✅ DONE (2026-07-11)

**Resolution note**: AddServiceDefaults()/health checks/resilience were already wired in ServiceDefaults+Program.cs. Added: `IsAspireProjectResource="false"` on the AppHost→ServiceDefaults reference (fixes ASPIRE004 warning deferred from T02); `Aspire.Hosting.Testing` package; an aliased (`AppHostRef`) ProjectReference from Tests.Integration to AppHost (avoids `Program` type clash with the Api project); `AppHostTests.cs` using `DistributedApplicationTestingBuilder` to boot the AppHost and assert `/health` responds successfully. Full gate passed (build 0 errors, integration test green in ~45s).

**What**: Wire Aspire AppHost project with ServiceDefaults — OpenTelemetry, health checks, service discovery defaults applied to the API project
**Where**: `01-aspire/RentifyX.Communications.AppHost/`, `01-aspire/RentifyX.Communications.ServiceDefaults/`
**Depends on**: T02, T03
**Reuses**: Existing `01-aspire/` directory already present in repo
**Requirement**: E01-09

**Tools**:
- MCP: context7 (Aspire AppHost API if needed)
- Skill: none

**Done when**:
- [ ] AppHost project references the API project
- [ ] ServiceDefaults project exists with `AddServiceDefaults()` extension (OTEL, health checks, resilience)
- [ ] API project calls `builder.AddServiceDefaults()` in Program.cs
- [ ] `dotnet run --project AppHost` starts the API without errors (Aspire dashboard accessible)
- [ ] Integration test: AppHost creates and the API project starts (using `DistributedApplicationTestingBuilder`)

**Tests**: integration
**Gate**: full — `dotnet test`

**Verify**: `dotnet run --project 01-aspire/RentifyX.Communications.AppHost` → Aspire dashboard loads, API resource shows as Running

**Commit**: `feat(aspire): configure AppHost and ServiceDefaults with OTEL and health checks`

---

### T05: Configure API host baseline (Serilog, CorrelationId, health endpoint, Scalar UI, ErrorOr) — ✅ DONE (2026-07-11)

**Resolution note**: CorrelationIdMiddleware, Scalar UI, and ErrorOr-in-Application already existed. Changed: Serilog console sink switched from a human-readable outputTemplate to `JsonFormatter` (unconditionally, per explicit user decision — structured JSON in all environments, not just production); added ErrorOr to Domain.csproj; added a JSON `ResponseWriter` to ServiceDefaults' `/health` and `/alive` mappings so they return `{"status":"Healthy"}` instead of plain text; created a new `RentifyxCommunications.Tests.Api` project (`03-tests/06-Api/`) since no existing test project referenced the API layer for middleware unit tests, with 2 tests for `CorrelationIdMiddleware` (new ID generation, existing ID propagation). Both new tests pass; AppHost integration test re-verified green with the new health writer.

**What**: Program.cs baseline — Serilog structured JSON, CorrelationId middleware, `/health` endpoint, Scalar UI at `/scalar`, ErrorOr<T> NuGet reference
**Where**: `02-src/RentifyX.Communications.API/Program.cs`, `appsettings.json`
**Depends on**: T04
**Reuses**: ServiceDefaults from T04 (health checks already wired via AddServiceDefaults)
**Requirement**: E01-03, E01-04, E01-06, E01-07

**Tools**:
- MCP: context7 (Serilog.AspNetCore, Scalar.AspNetCore setup)
- Skill: none

**Done when**:
- [ ] Serilog configured as logging provider with `WriteTo.Console(formatter: JsonFormatter)` for structured output
- [ ] `CorrelationIdMiddleware` attaches `X-Correlation-Id` header to every request and propagates it to response
- [ ] `GET /health` returns `200 OK` with `{"status":"Healthy"}`
- [ ] `GET /scalar` returns Scalar UI HTML in Development environment
- [ ] `ErrorOr` NuGet package added to Application/Domain projects (not just API)
- [ ] Unit test: `CorrelationIdMiddleware` generates a new ID when none present; propagates existing ID when header present
- [ ] Quick gate passes

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Verify**: `curl http://localhost:{port}/health` → `{"status":"Healthy"}`; start in Development → `curl /scalar` returns HTML

**Commit**: `feat(api): configure Serilog, CorrelationId middleware, health endpoint, and Scalar UI`

---

### T06: Implement GlobalExceptionHandler (ProblemDetails, no stack trace in prod) — ✅ DONE (2026-07-11)

**Resolution note**: The existing handler unconditionally leaked `exception.Message`/`exception.GetType().FullName` in `Extensions` regardless of environment — a real security gap (exception details exposed to clients in Production). Fixed by injecting `IHostEnvironment` and setting `ProblemDetails.Detail` conditionally (exception message in Development, a generic message in Production), removing the always-on `exceptionType`/`exceptionMessage` extensions. Also found and fixed a real bug during RED-phase testing: `WriteAsJsonAsync` was silently overwriting the `application/problem+json` Content-Type with `application/json` — fixed by passing `contentType` explicitly to `WriteAsJsonAsync`. Added `InternalsVisibleTo` for `RentifyxCommunications.Tests.Api` (handler is `internal`). 5/5 tests pass (2 pre-existing CorrelationId + 3 new for this handler).

**What**: `GlobalExceptionHandler` implementing `IExceptionHandler` — maps unhandled exceptions to RFC 7807 `ProblemDetails`; suppresses stack traces outside Development
**Where**: `02-src/RentifyX.Communications.API/Middleware/GlobalExceptionHandler.cs`
**Depends on**: T05
**Reuses**: Pattern from identity-api GlobalExceptionHandler (adapt, do not copy blindly)
**Requirement**: E01-05

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `GlobalExceptionHandler` implements `IExceptionHandler` and is registered in Program.cs
- [ ] Unhandled exception returns `application/problem+json` with `status`, `title`, `detail`
- [ ] `detail` field is suppressed (returns generic message) when `IHostEnvironment.IsProduction()` is true
- [ ] `detail` field includes exception message when `IHostEnvironment.IsDevelopment()` is true
- [ ] Correlation ID from `ICorrelationIdAccessor` is included in the ProblemDetails `extensions`
- [ ] Unit test: exception in Development → ProblemDetails with detail; exception in Production → ProblemDetails without stack trace
- [ ] Unit test: ProblemDetails response has correct `Content-Type: application/problem+json`
- [ ] Quick gate passes (≥2 unit tests)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Verify**: POST to a route that throws → response body is valid ProblemDetails JSON; status 500; no stack trace when env=Production

**Commit**: `feat(api): implement GlobalExceptionHandler returning RFC 7807 ProblemDetails`

---

### T07: Configure AWS SDK against real dev/sandbox account — ✅ DONE (2026-07-12, REWORKED per AD-012)

**Resolution note**: Wired in `InfrastructureDependencyInjection.AddAwsOptions` (not `AppHost.cs` — Aspire's orchestration file has no need to touch AWS credentials; the API process resolves them at DI-registration time, which is where the fail-fast check also lives). Added `AWSSDK.Extensions.NETCore.Setup` package. Fail-fast implemented as two checks before `AddDefaultAWSOptions`: (1) `AWS:Profile` config key missing/blank → throws with a clear message; (2) `CredentialProfileStoreChain.TryGetAWSCredentials` fails to resolve the named profile → throws naming the profile and the `aws configure` fix. Verified manually: running the API with no profile configured fails fast with the clear message (not a raw SDK stack trace) — see terminal output at 2026-07-12T12:32 in session log. `AWS:Profile`/`AWS:Region` placeholders added to `appsettings.Development.json` (`Profile` left empty — developer sets the real value via `dotnet user-secrets`, per the "never committed" requirement). `dotnet build --no-incremental` → 0 errors, 0 warnings.

**What**: Wire `AWSSDKConfig` (region + named credentials profile) in the AppHost so the API and any AWS SDK clients (DynamoDB, SES, SecretsManager, KMS) resolve against a real AWS dev/sandbox account — no LocalStack container. The profile name is read from configuration (e.g. `AWS:Profile`), never hardcoded.
**Where**: `01-aspire/RentifyxCommunications.AppHost/AppHost.cs`; `02-src/01-Api/RentifyxCommunications.Api/appsettings.Development.json` (profile name + region only — no secrets)
**Depends on**: T04
**Reuses**: `AWSSDK.Extensions.NETCore.Setup` (`AddDefaultAWSOptions`) for standard SDK credential resolution via named profile
**Requirement**: E01-09

**Original LocalStack plan superseded**: See `.specs/project/STATE.md` AD-012 and AD-013. Manual dev run always uses the real AWS dev/sandbox account (this task). Per AD-013, automated integration tests use LocalStack instead (see T12) — LocalStack is not used for manual dev/AppHost runs.

**Tools**:
- MCP: context7 (AWSSDK.Extensions.NETCore.Setup — AddDefaultAWSOptions / AWSOptions patterns)
- Skill: none

**Done when**:
- [ ] `AWS:Profile` and `AWS:Region` configuration keys documented in `appsettings.Development.json` (values are placeholders — actual profile name is developer-specific, set via `dotnet user-secrets` or local env, never committed)
- [ ] API startup registers `AWSOptions` from the named profile via `AddDefaultAWSOptions`
- [ ] Missing/invalid AWS credentials at startup produce a clear, fail-fast error (not a hang or silent no-op) — ties into E01-25's fail-fast pattern
- [ ] `dotnet run --project AppHost` starts the API without errors when a valid profile is configured locally

**Tests**: none (credential resolution is exercised by T12's integration tests against a LocalStack Secrets Manager container, per AD-013)
**Gate**: build — `dotnet build --no-incremental`

**Verify**: With a valid local AWS profile configured, `dotnet run --project AppHost` starts cleanly; with an invalid/missing profile, startup fails with a clear error naming the missing credential source (not a raw SDK stack trace).

**Commit**: `feat(aspire): configure AWS SDK against real dev/sandbox account via named profile`

---

### T08: Document dev-account AWS resource requirements — ✅ DONE (2026-07-12, REWORKED per AD-012)

**Resolution note**: The "AWS Dev Account Requirements" section already existed in `docs/architecture/overview.md` (added in an earlier commit alongside AD-012). Updated it for AD-013: the `Environments` table's CI row now states LocalStack resolves the credential question (previously "not yet decided"), and both the environments table and the requirements intro clarify these dev-account resources are for manual `dotnet run --project AppHost` sessions only — automated tests use LocalStack, not this account. All 5 `Done when` items verified against the current doc content.

**What**: Document the AWS resources this service expects to already exist in the dev/sandbox account (DynamoDB tables, SES sender identity, Secrets Manager entries) — provisioning itself is manual and out of scope for this task (per user decision, 2026-07-11). This replaces the original LocalStack init script.
**Where**: `docs/architecture/overview.md` (new "AWS Dev Account Requirements" section) — no code, no init script
**Depends on**: T07
**Reuses**: Naming already defined elsewhere in specs — `notifications`/`delivery-log` tables (E-04 design), SES sender identity, `rentifyx/comms/*` secret keys (README Secrets section)
**Requirement**: E01-10

**Original LocalStack plan superseded**: No init script exists or is planned — real AWS resources must be provisioned once (manually, or later via the E-06 Terraform module) before the app can run end-to-end.

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Doc lists required DynamoDB tables (`notifications`: PK=`NOTIF#{id}`; `delivery-log`: PK=`LOG#{id}`) with billing mode and key schema
- [ ] Doc lists the required SES sender identity (domain/email) and notes it must be verified in the dev account before sends succeed
- [ ] Doc lists required Secrets Manager entries (`rentifyx/comms/ses-arn`, `rentifyx/comms/kafka-sasl-username`, `rentifyx/comms/kafka-sasl-password`)
- [ ] Doc explicitly states these are NOT auto-provisioned by this service — a manual step (or the future E-06 Terraform apply) is required first
- [ ] Doc notes these dev-account resources are only needed for manual `dotnet run --project AppHost` sessions — automated tests use LocalStack (AD-013) and don't require them

**Tests**: none
**Gate**: build — n/a (docs only, no build impact)

**Verify**: Manual review — a new developer reading the doc knows exactly what to create in the dev account before running the app.

**Commit**: `docs(infra): document required AWS dev-account resources (DynamoDB, SES, Secrets Manager)`

---

### T09: Add Kafka container to Aspire AppHost [P] — ✅ DONE (2026-07-12)

**Resolution note**: `builder.AddKafka("kafka")` registered in AppHost (KRaft mode, no ZooKeeper, per current Aspire Kafka hosting docs) and referenced from the API project via `.WithReference(kafka)`. Along the way fixed `AppHostTests.cs`, which still referenced the stale `"clean-arch-api"` resource name from the `a34a23f` rename and was failing as a result. Added `AppHost_StartsKafkaResource_AndBrokerIsReachable` (producer connects and persists a message) per this task's "Done when". `dotnet build --no-incremental` → 0 errors, 0 warnings. Verification of both `AppHostTests` (health-check + Kafka) required Docker Desktop up and `AWS:Profile` set via `dotnet user-secrets` on the Api project — the health-check test was silently broken since T07 introduced its fail-fast AWS credential check (T07 landed after T04, which originally added that test); added `UserSecretsId` to `RentifyxCommunications.Api.csproj` to unblock this locally. Both tests pass. Gap between this local-only fix and CI (which has no real AWS profile) tracked in `.specs/project/STATE.md` Todos.

**What**: Register Kafka container in AppHost (KRaft mode, no ZooKeeper); expose broker address to API project as configuration
**Where**: `01-aspire/RentifyX.Communications.AppHost/Program.cs`
**Depends on**: T04
**Reuses**: `Aspire.Hosting.Kafka` NuGet package
**Requirement**: E01-09, E01-12

**Tools**:
- MCP: context7 (Aspire.Hosting.Kafka)
- Skill: none

**Done when**:
- [ ] Kafka container added to AppHost via `builder.AddKafka("kafka")`
- [ ] Kafka connection string injected into API project
- [ ] AppHost starts with Kafka container running
- [ ] Integration test: AppHost boots and Kafka broker is reachable (producer can connect)

**Tests**: integration
**Gate**: full — `dotnet test`

**Verify**: `dotnet run --project AppHost` → Aspire dashboard shows Kafka resource as Running

**Commit**: `feat(aspire): add Kafka container (KRaft mode) to AppHost`

---

### T10: Implement NotificationRequestedConsumer as IHostedService skeleton — ✅ DONE (2026-07-13)

**Resolution note**: `NotificationRequestedConsumer` added under `02-src/01-Api/RentifyxCommunications.Api/Consumers/`, along with `IKafkaConsumerFactory`/`KafkaConsumerFactory` (reads `ConnectionStrings:kafka`, the same key `AppHostTests` already resolves via `GetConnectionStringAsync("kafka")`) — introduced as a seam so unit tests can mock consumer creation without a live broker. `StartAsync` retries up to 3 times with exponential backoff (2s/4s in production; overridable via an internal constructor parameter so tests run near-instantly) and never throws even if all attempts fail — it just logs `Error` and leaves the consumer unstarted, matching "skeleton, no crash" intent. Consume loop polls with a 1s timeout, commits offsets manually (`EnableAutoCommit=false`), does no message processing (explicit comment marks the E-03 injection point). `StopAsync` cancels the loop, waits up to 30s, then closes the consumer. Registered in `Program.cs` alongside the other `builder.Services.Add*` calls. 3 unit tests added in `Tests.Api/Consumers/` (added `Moq` package reference to that test project, matching the pattern already used in `Tests.Common`/`Tests.Handlers`/`Tests.Repositories`) — mocked `IKafkaConsumerFactory`/`IConsumer<Ignore,string>`, no real broker needed. Verified end-to-end too: re-ran `AppHostTests` (real Docker + Kafka container) — both still pass, confirming the consumer doesn't crash or hang real host startup.

**What**: `NotificationRequestedConsumer` — IHostedService that subscribes to `notification-requested` Kafka topic, logs startup/shutdown, retries on connection failure with backoff; no message processing logic yet
**Where**: `02-src/RentifyX.Communications.API/Consumers/NotificationRequestedConsumer.cs`
**Depends on**: T05, T09
**Reuses**: Confluent.Kafka NuGet for consumer client
**Requirement**: E01-13, E01-14, E01-15, E01-16

**Tools**:
- MCP: context7 (Confluent.Kafka IConsumer)
- Skill: none

**Done when**:
- [ ] `NotificationRequestedConsumer` implements `IHostedService`
- [ ] `StartAsync`: creates Kafka consumer, subscribes to `notification-requested`, logs `Information` with consumer group ID + topic name
- [ ] `ExecuteAsync` (or consume loop): poll loop that does nothing with messages yet (just commits offset); designed for E-03 to inject processing logic
- [ ] `StopAsync`: calls `consumer.Close()` after draining in-flight (waits for consume loop to finish, max 30s)
- [ ] Kafka connection failure on startup: logs `Error`, retries with exponential backoff (3 retries), does NOT throw and crash the host
- [ ] Registered as `IHostedService` in `Program.cs`
- [ ] Unit test: `StartAsync` logs expected messages (mock ILogger)
- [ ] Unit test: `StopAsync` sets cancellation and consume loop exits cleanly (no hang >1s)
- [ ] Unit test: Kafka connection failure does not propagate exception out of `StartAsync`
- [ ] Quick gate passes (≥3 unit tests)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Verify**: `dotnet run --project AppHost` → startup logs show `NotificationRequestedConsumer subscribed to notification-requested (group: ...)`; `Ctrl+C` → logs show graceful consumer shutdown

**Commit**: `feat(consumer): register NotificationRequestedConsumer IHostedService skeleton with graceful lifecycle`

---

### T11: Define ISecretsProvider interface + Application layer registration [P] — ✅ DONE (2026-07-12)

**Resolution note**: `ISecretsProvider` and `SecretsProviderOptions` added under `RentifyxCommunications.Application/Abstractions/`. `SecretsProviderOptions` holds the three Secrets Manager *key names* (not values), defaulted to the entries documented in T08 (`rentifyx/comms/ses-arn`, `rentifyx/comms/kafka-sasl-username`, `rentifyx/comms/kafka-sasl-password`). Application project already had zero Infrastructure reference (only Domain), so that constraint was satisfied by construction. 3 new unit tests added in `RentifyxCommunications.Tests.Handlers/Abstractions/SecretsProviderTests.cs` (assembly/namespace placement, method signature via reflection, options defaults). No DI registration added — there's no implementation to register yet (that's T12).

**What**: `ISecretsProvider` interface in Application layer with `GetSecretAsync(key)` method; typed wrapper over Secrets Manager; no implementation yet
**Where**: `02-src/RentifyX.Communications.Application/Abstractions/ISecretsProvider.cs`
**Depends on**: T01
**Reuses**: Interface pattern from identity-api (contract shape, not implementation)
**Requirement**: E01-23

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `ISecretsProvider` interface defined: `Task<string> GetSecretAsync(string key, CancellationToken ct = default)`
- [ ] Interface lives in Application layer (zero infrastructure references)
- [ ] `SecretsProviderOptions` record defined: `SesArn`, `KafkaSaslUsername`, `KafkaSaslPassword` (keys, not values)
- [ ] Unit test: interface is in correct namespace and assembly; options record has expected properties
- [ ] Quick gate passes

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Verify**: `dotnet build` → zero errors; interface resolves in Application project without Infrastructure reference

**Commit**: `feat(application): define ISecretsProvider interface and SecretsProviderOptions`

---

### T12: Implement SecretsManagerProvider + wire into API startup (REWORKED per AD-013, 2026-07-12)

**What**: `SecretsManagerProvider` implementing `ISecretsProvider` — at runtime resolves against the real AWS Secrets Manager in the dev/sandbox account (via the same named profile as T07), 5-min in-memory cache, fail-fast on missing secrets at startup. Its integration test suite runs against a **LocalStack Secrets Manager container** (Testcontainers), not the real dev account (AD-013) — the client's endpoint is overridden to the LocalStack container URL only in the test host, never in `Program.cs`.
**Where**: `02-src/RentifyX.Communications.Infrastructure/Secrets/SecretsManagerProvider.cs`; registered in `Program.cs`; test fixture in `03-tests/05-Integration` wires the LocalStack container
**Depends on**: T05, T11
**Reuses**: `AWSSDK.SecretsManager` NuGet; `IMemoryCache` for TTL caching; `Testcontainers.LocalStack` NuGet for the integration test fixture
**Requirement**: E01-22, E01-24, E01-25, E01-26, E01-27, E01-28

**Tools**:
- MCP: context7 (AWSSDK.SecretsManager; Testcontainers.LocalStack)
- Skill: none

**Done when**:
- [ ] `SecretsManagerProvider` implements `ISecretsProvider`; at runtime resolves against the real AWS Secrets Manager using the dev/sandbox account's named credentials profile (T07) — no LocalStack-specific code in `Program.cs` or the provider itself
- [ ] `GetSecretAsync`: retrieves secret, caches value in `IMemoryCache` with 5-minute absolute expiry
- [ ] Startup validation: on host start, resolves `SecretsProviderOptions` keys (`SesArn`, `KafkaSaslUsername`, `KafkaSaslPassword`) — if any secret is missing, logs `Critical` and throws (fail fast)
- [ ] `ISecretsProvider` registered in DI with `SecretsManagerProvider` implementation
- [ ] Integration test fixture spins up a LocalStack container (Testcontainers) with Secrets Manager enabled and seeds the required secrets before each test
- [ ] Integration test: LocalStack Secrets Manager has required secrets → startup succeeds, `GetSecretAsync` returns correct values
- [ ] Integration test: LocalStack Secrets Manager missing a required secret → startup throws with `Critical` log entry
- [ ] Integration test: second call within 5 min uses cache (Secrets Manager called only once)
- [ ] Full gate passes

**Tests**: integration
**Gate**: full — `dotnet test`

**Note**: Per AD-013, these integration tests run against LocalStack, not the real AWS dev/sandbox account — this also resolves the previously-open CI credential-strategy question (CI needs no real AWS credentials to run this suite, just a LocalStack container).

**Verify**: Start AppHost, remove a secret from the dev account's Secrets Manager → API logs `[Critical] Required secret 'SesArn' not found. Startup aborted.` and exits non-zero

**Commit**: `feat(infra): implement SecretsManagerProvider with 5-min cache and fail-fast startup validation`

---

### T13: Create GitHub Actions CI workflow (build + test + coverage) [P] — ✅ DONE (2026-07-12)

**Resolution note**: A prior `.github/workflows/ci.yml` already existed (build+test, no coverage) but triggered on `master`, not `main` — fixed. Test step scoped to `--filter "Category!=Integration"` rather than the full suite: `AppHostTests` boots the real API via `DistributedApplicationTestingBuilder` and needs Docker + a real AWS profile (see T09/STATE.md Todo), neither available in CI, so running the full suite would make every CI run fail regardless of code correctness — consistent with AD-013 ("CI needs no real AWS credentials"). This surfaced that no test in the repo actually carried a `Category=Integration` trait (the documented "quick" gate filter was a no-op) — added `[Trait("Category", "Integration")]` to `AppHostTests` as a class-level attribute to make the filter real; verified locally that it now excludes those tests. Coverage: `--collect:"XPlat Code Coverage"` (coverlet, already referenced in every test project) → `reportgenerator` (JsonSummary) → a Node one-liner reads `summary.linecoverage` from `Summary.json` and fails the step below 80%. **Current repo coverage is ~5.6%** (E-01 is foundational scaffolding; the `Examples` feature is template boilerplate with placeholder `[Fact(Skip=...)]` tests) — the gate is intentionally on with the real 80% threshold per explicit user decision, so CI will be red until real tests land alongside E-02+ feature work, rather than quietly deferring the debt. `actionlint` isn't installed locally; validated YAML syntax with `npx js-yaml` instead (semantic step/action correctness will be confirmed on first real CI run).

**What**: `.github/workflows/ci.yml` — build → test → coverage gate ≥80%; produces artifacts for test results and coverage report
**Where**: `.github/workflows/ci.yml`
**Depends on**: T01
**Reuses**: `.github/` directory already present in repo (check existing workflows)
**Requirement**: E01-17, E01-18

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Workflow triggers on `push` to `main` and `pull_request` targeting `main`
- [ ] Steps: `actions/checkout` → `actions/setup-dotnet@v4` (.NET 10) → `dotnet restore` → `dotnet build --no-restore` → `dotnet test --collect:"XPlat Code Coverage"`
- [ ] Coverage gate: uses `reportgenerator` or `dotnet-coverage` to enforce ≥80% line coverage; fails workflow if below threshold
- [ ] Test results artifact uploaded (`actions/upload-artifact`)
- [ ] Coverage report artifact uploaded
- [ ] Workflow file passes `actionlint` (or equivalent YAML lint)

**Tests**: none
**Gate**: build — `dotnet build --no-incremental` (workflow file is YAML, not compiled; verify via CI run)

**Verify**: Push branch → GitHub Actions run shows all steps pass; introduce a coverage drop < 80% → workflow fails on coverage step

**Commit**: `ci: add GitHub Actions workflow with build, test, and 80% coverage gate`

---

### T14: Add Dockerfile + Trivy image scan step to CI [P]

**What**: Multi-stage `Dockerfile` for the API; add Trivy scan step to CI workflow that fails on HIGH/CRITICAL vulnerabilities
**Where**: `Dockerfile` (repo root — already exists per git status, read first); `.github/workflows/ci.yml`
**Depends on**: T13
**Reuses**: Existing `Dockerfile` in repo root (read and extend rather than replace)
**Requirement**: E01-20

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Dockerfile uses multi-stage build: `sdk` stage for build/publish, `aspnet` (runtime) stage for final image
- [ ] Final image runs as non-root user
- [ ] CI workflow: after `dotnet build`, adds `aquasecurity/trivy-action` step scanning the built image
- [ ] Trivy step fails workflow on `HIGH` or `CRITICAL` severity findings
- [ ] Trivy scan report uploaded as workflow artifact
- [ ] `docker build .` succeeds locally

**Tests**: none
**Gate**: build — `docker build .`

**Verify**: `docker build -t rentifyx-comms:test .` → succeeds; `docker run --user 1000 ...` works (non-root)

**Commit**: `ci: add multi-stage Dockerfile and Trivy image vulnerability scan`

---

### T15: Add OWASP dependency-check step to CI [P]

**What**: Add `dependency-check` step to CI workflow using `jeremylong/DependencyCheck` action; fails on HIGH/CRITICAL CVEs in direct and transitive NuGet dependencies
**Where**: `.github/workflows/ci.yml`
**Depends on**: T13
**Reuses**: OWASP dependency-check GitHub Action
**Requirement**: E01-19

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `dependency-check/Dependency-Check_Action` step added to CI workflow after `dotnet restore`
- [ ] Scan includes transitive NuGet dependencies (not just direct)
- [ ] Workflow fails if any finding has CVSS score ≥ 7 (HIGH or CRITICAL)
- [ ] OWASP report (HTML + JSON) uploaded as workflow artifact
- [ ] Step uses NVD API key secret (`NVD_API_KEY`) to avoid rate limiting (secret must be documented in README)

**Tests**: none
**Gate**: build — CI run (YAML only)

**Verify**: Add a known-vulnerable NuGet package (e.g., an old version with published CVE) to a test branch → CI fails on OWASP step

**Commit**: `ci: add OWASP dependency-check for HIGH/CRITICAL CVE detection`

---

### T16: Configure GitHub branch protection rules

**What**: Branch protection on `main` requiring CI workflow green + at least 1 approved PR review before merge
**Where**: GitHub repository settings (via `gh` CLI or GitHub API — not a file change)
**Depends on**: T14, T15
**Reuses**: none
**Requirement**: E01-21

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `main` branch requires status check: `CI / build-test-coverage` must pass
- [ ] `main` branch requires status check: `CI / trivy-scan` must pass
- [ ] `main` branch requires status check: `CI / owasp-check` must pass
- [ ] `main` branch requires at least 1 approved review before merge
- [ ] Direct push to `main` is disabled (force push requires admin override)

**Tests**: none
**Gate**: manual — verify in GitHub repository Settings → Branches

**Verify**: Attempt a direct push to `main` without a PR → rejected; open a PR without CI green → merge button disabled

**Commit**: N/A (GitHub settings change, not a code commit)

---

### T17: Configure git-secrets pre-commit hook [P] — ✅ DONE (2026-07-12)

**Resolution note**: The Chocolatey `git-secrets` package no longer exists (`choco search git-secrets` returns nothing) — README's Windows install instructions were stale and have been fixed to fetch the script directly from `awslabs/git-secrets` on GitHub instead. `git secrets --register-aws` run against this repo (registers 6 AWS patterns via local `.git/config`, per-machine, not repo-tracked — expected). Extended the existing `.hooks/pre-commit` (already used for the build-check hook, `core.hooksPath` already `.hooks`) rather than running `git secrets --install`, since that templates into `.git/hooks/` directly and would be silently bypassed by the custom hooksPath — this matches the task's own "(or hook script added to .hooks/pre-commit)" alternative. Hook now fails closed if `git-secrets` isn't installed (clear message pointing to README), then runs `git secrets --scan --cached` before the existing build-check step. **Verified with a genuinely fake key** (`AKIAABCDEFGHIJKLMNOP`) — blocked as expected. The literal test key from this task's own "Verify" line, `AKIAIOSFODNN7EXAMPLE`, is on git-secrets' own built-in allowlist (it's AWS's official documentation placeholder, excluded by `--register-aws` itself to avoid false positives) — confirmed it does **not** block, which deviates from the literal Done-when scenario but is the tool's intended, correct behavior, not a bug.

**What**: Install `git-secrets`, register AWS secret patterns, add pre-commit hook that blocks commits containing secret patterns
**Where**: `.hooks/` directory (already present in repo per git status); `.git/hooks/pre-commit`
**Depends on**: T01
**Reuses**: `.hooks/` directory already in repo (check contents first)
**Requirement**: E01-27

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `git-secrets --install` run in repo (or hook script added to `.hooks/pre-commit`)
- [ ] AWS credential patterns registered: `git secrets --register-aws`
- [ ] Hook script documented in README: `git config core.hooksPath .hooks` required for each dev machine
- [ ] Test: stage a file containing `AKIAIOSFODNN7EXAMPLE` (fake AWS key) → `git commit` is blocked with error message
- [ ] Test: stage a normal code file → `git commit` proceeds normally

**Tests**: none
**Gate**: manual — run the verify steps

**Verify**: `echo "AKIAIOSFODNN7EXAMPLE" > /tmp/test-secret.txt && git add /tmp/test-secret.txt && git commit -m "test"` → blocked by hook with `[ERROR] Matched one or more prohibited patterns`

**Commit**: `chore: add git-secrets pre-commit hook for AWS credential pattern detection`

---

## Parallel Execution Map

```
Phase 1 — Bootstrap (Sequential — each builds on the previous):
  T01 ──→ T02 ──→ T03

Phase 2 — Parallel Tracks (all start after T03 completes):
  T03 done, then simultaneously:
    Track A: T04
    Track B: T11 [P]    (Application layer only, no Aspire dependency)
    Track C: T13 [P]    (CI YAML only, no project dependency beyond solution existing)
    Track D: T17 [P]    (git hook, no code dependency)

Phase 3 — Build on Phase 2 (each track continues):
  T04 done:
    ├── T05 (API baseline — sequential, touches Program.cs heavily)
    ├── T07 (AWS SDK dev-account config — sequential, Aspire AppHost)
    └── T09 [P with T07] (Kafka container — parallel with T07, different AppHost additions)

  T13 done:
    ├── T14 [P] (Dockerfile + Trivy)
    └── T15 [P] (OWASP step — parallel with T14, different CI steps)

Phase 4 — Integration (all after their respective deps):
  T05 done:
    └── T06 (GlobalExceptionHandler — depends only on T05)

  T07 done:
    └── T08 (dev-account resource docs)

  T05 + T09 done:
    └── T10 (Kafka consumer skeleton)

  T05 + T11 done:
    └── T12 (SecretsManagerProvider + wiring)

  T14 + T15 done:
    └── T16 (branch protection — depends on CI steps existing)
```

**Note on integration test parallelism**: T04, T07, T08, T09, T12 all have integration tests (Testcontainers). These run sequentially within the test suite (`[Collection]` fixtures). Do NOT mark these `[P]` — their test execution is the bottleneck.

---

## Granularity Check

| Task | Scope | Status |
|---|---|---|
| T01: Create solution + project files | 1 scaffold operation (6 project files) | ✅ Granular |
| T02: Directory.Build.props + Packages.props | 2 config files, one concern (build settings) | ✅ Granular |
| T03: .editorconfig CA5xxx rules | 1 file, specific rule additions | ✅ Granular |
| T04: Aspire AppHost + ServiceDefaults | 1 concern (Aspire wiring) across 2 files | ✅ Granular |
| T05: API host baseline (Serilog + CorrelationId + health + Scalar + ErrorOr) | Multiple middleware, but all in Program.cs baseline registration | ⚠️ Borderline — kept together because they're all baseline Program.cs setup with no logic branches; splitting would create merge conflicts across Program.cs |
| T06: GlobalExceptionHandler | 1 class + 1 registration | ✅ Granular |
| T07: AWS SDK dev-account config | 1 AppHost addition | ✅ Granular |
| T08: dev-account resource docs | 1 doc section, 1 concern | ✅ Granular |
| T09: Kafka container in AppHost | 1 AppHost addition | ✅ Granular |
| T10: NotificationRequestedConsumer skeleton | 1 class (IHostedService) | ✅ Granular |
| T11: ISecretsProvider interface | 1 interface + 1 options record | ✅ Granular |
| T12: SecretsManagerProvider + wiring | 1 implementation class + DI registration | ✅ Granular |
| T13: GitHub Actions CI workflow | 1 YAML file | ✅ Granular |
| T14: Dockerfile + Trivy step | 1 Dockerfile + 1 CI step addition | ✅ Granular |
| T15: OWASP step | 1 CI step addition | ✅ Granular |
| T16: Branch protection | 1 GitHub settings operation | ✅ Granular |
| T17: git-secrets hook | 1 hook script + registration | ✅ Granular |

**T05 note**: Kept combined because all items register in `Program.cs` during the same "baseline setup" pass. Splitting them would require multiple PR-conflicting edits to the same file. Each item has a separate unit test to preserve individual verifiability.

---

## Diagram–Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
|---|---|---|---|
| T01 | None | Phase 1 start | ✅ Match |
| T02 | T01 | T01 → T02 | ✅ Match |
| T03 | T01 | T02 → T03 | ✅ Match |
| T04 | T02, T03 | After T03 (Track A) | ✅ Match |
| T05 | T04 | T04 → T05 | ✅ Match |
| T06 | T05 | T05 → T06 | ✅ Match |
| T07 | T04 | T04 → T07 | ✅ Match |
| T08 | T07 | T07 → T08 | ✅ Match |
| T09 | T04 | T04 → T09 [P with T07] | ✅ Match |
| T10 | T05, T09 | T05 + T09 → T10 | ✅ Match |
| T11 | T01 | After T03 (Track B) [P] | ✅ Match |
| T12 | T05, T11 | T05 + T11 → T12 | ✅ Match |
| T13 | T01 | After T03 (Track C) [P] | ✅ Match |
| T14 | T13 | T13 → T14 [P] | ✅ Match |
| T15 | T13 | T13 → T15 [P] | ✅ Match |
| T16 | T14, T15 | T14 + T15 → T16 | ✅ Match |
| T17 | T01 | After T03 (Track D) [P] | ✅ Match |

All ✅ — no mismatches.

---

## Test Co-location Validation

*(TESTING.md not yet created — test types derived from code layer + E-01 plan context)*

| Task | Code Layer Created/Modified | Test Type Required | Task Says | Status |
|---|---|---|---|---|
| T01 | Scaffold (no logic) | none | none | ✅ OK |
| T02 | MSBuild config (no logic) | none | none | ✅ OK |
| T03 | Editor config (no logic) | none | none | ✅ OK |
| T04 | Aspire host wiring | integration | integration | ✅ OK |
| T05 | Middleware pipeline (logic: CorrelationId) | unit | unit | ✅ OK |
| T06 | Exception handling middleware (logic) | unit | unit | ✅ OK |
| T07 | Infrastructure container config | integration | integration | ✅ OK |
| T08 | Init script (AWS resource creation) | integration | integration | ✅ OK |
| T09 | Infrastructure container config | integration | integration | ✅ OK |
| T10 | IHostedService consumer lifecycle (logic) | unit | unit | ✅ OK |
| T11 | Interface + options record (no logic) | unit | unit | ✅ OK |
| T12 | Infrastructure provider + startup (logic) | integration | integration | ✅ OK |
| T13 | CI workflow YAML (no code logic) | none | none | ✅ OK |
| T14 | Dockerfile + CI step | none | none | ✅ OK |
| T15 | CI step | none | none | ✅ OK |
| T16 | GitHub settings (no code) | none | none | ✅ OK |
| T17 | Git hook script | none | none | ✅ OK |

All ✅ — no test co-location violations.

---

## Requirement Traceability Update

| Requirement ID | Mapped to Task | Status |
|---|---|---|
| E01-01 | T01 | Done — verified via existing scaffold reuse (`dotnet build` 0 errors); naming/test-split divergences noted in session log |
| E01-02 | T02 | Done — props already correct; ASPIRE004 warning deferred to T04 |
| E01-03 | T05 | Done |
| E01-04 | T05 | Done |
| E01-05 | T06 | Done |
| E01-06 | T05 | Done |
| E01-07 | T05 | Done |
| E01-08 | T03 | Done — CA5xxx rules already enforced |
| E01-09 | T04, T07, T09 | T04 done; T07/T09 pending |
| E01-10 | T07, T08 | Pending |
| E01-11 | T08 | Pending |
| E01-12 | T09 | Pending |
| E01-13 | T10 | Pending |
| E01-14 | T10 | Pending |
| E01-15 | T10 | Pending |
| E01-16 | T10 | Pending |
| E01-17 | T13 | Pending |
| E01-18 | T13 | Pending |
| E01-19 | T15 | Pending |
| E01-20 | T14 | Pending |
| E01-21 | T16 | Pending |
| E01-22 | T12 | Pending |
| E01-23 | T11 | Pending |
| E01-24 | T12 | Pending |
| E01-25 | T12 | Pending |
| E01-26 | T12 | Pending |
| E01-27 | T12, T17 | Pending |
| E01-28 | T12 | Pending |

**Coverage:** 28 requirements, 28 mapped to tasks ✅
