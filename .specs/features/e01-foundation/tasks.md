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

### T05: Configure API host baseline (Serilog, CorrelationId, health endpoint, Scalar UI, ErrorOr)

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

### T06: Implement GlobalExceptionHandler (ProblemDetails, no stack trace in prod)

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

### T07: Add LocalStack container to Aspire AppHost

**What**: Register LocalStack container in AppHost with DynamoDB, SES, SecretsManager, KMS services enabled; expose connection to API project
**Where**: `01-aspire/RentifyX.Communications.AppHost/Program.cs`
**Depends on**: T04
**Reuses**: LocalStack container pattern (check Aspire.Hosting.LocalStack or Docker container resource)
**Requirement**: E01-09, E01-10

**Tools**:
- MCP: context7 (Aspire LocalStack or Docker container resource)
- Skill: none

**Done when**:
- [ ] LocalStack container defined in AppHost with `SERVICES=dynamodb,ses,secretsmanager,kms` env var
- [ ] LocalStack endpoint injected into API project as an environment variable (`AWS__ServiceURL` or equivalent)
- [ ] AppHost starts with LocalStack container running (`docker ps` shows localstack container)
- [ ] Integration test: AppHost boots and LocalStack health endpoint returns `{"status": "running"}`

**Tests**: integration
**Gate**: full — `dotnet test`

**Verify**: `dotnet run --project AppHost` → Aspire dashboard shows LocalStack resource as Running; `curl http://localhost:4566/_localstack/health` returns `{"status":"running"}`

**Commit**: `feat(aspire): add LocalStack container (DynamoDB, SES, SecretsManager, KMS)`

---

### T08: Create LocalStack init script (DynamoDB tables + SES identity)

**What**: Shell/Python init script that creates `notifications` DynamoDB table, `delivery-log` DynamoDB table, and a verified SES sender identity — runs on LocalStack startup, idempotent
**Where**: `01-aspire/localstack-init/init.sh` (or `.py`); wired into AppHost as a startup hook or volume mount
**Depends on**: T07
**Reuses**: LocalStack init pattern (volume mount `docker-entrypoint-initaws.d/`)
**Requirement**: E01-10, E01-11

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `notifications` table created: PK=`NOTIF#{id}` (S), billing mode PAY_PER_REQUEST
- [ ] `delivery-log` table created: PK=`LOG#{id}` (S), billing mode PAY_PER_REQUEST
- [ ] SES sender identity created for `no-reply@rentifyx.local`
- [ ] Script is idempotent: running twice does not error (uses `--no-fail-on-existing` or equivalent)
- [ ] Integration test: after AppHost boot, `aws dynamodb list-tables --endpoint-url http://localhost:4566` returns both table names
- [ ] Integration test: running init script twice produces no errors

**Tests**: integration
**Gate**: full — `dotnet test`

**Verify**: After `dotnet run --project AppHost`: `aws --endpoint-url=http://localhost:4566 dynamodb list-tables` shows `notifications` and `delivery-log`

**Commit**: `feat(aspire): add LocalStack init script for DynamoDB tables and SES identity`

---

### T09: Add Kafka container to Aspire AppHost [P]

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

### T10: Implement NotificationRequestedConsumer as IHostedService skeleton

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

### T11: Define ISecretsProvider interface + Application layer registration [P]

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

### T12: Implement SecretsManagerProvider + wire into API startup

**What**: `SecretsManagerProvider` implementing `ISecretsProvider` — loads from AWS Secrets Manager (LocalStack in dev), 5-min in-memory cache, fail-fast on missing secrets at startup
**Where**: `02-src/RentifyX.Communications.Infrastructure/Secrets/SecretsManagerProvider.cs`; registered in `Program.cs`
**Depends on**: T05, T11
**Reuses**: `AWSSDK.SecretsManager` NuGet; `IMemoryCache` for TTL caching
**Requirement**: E01-22, E01-24, E01-25, E01-26, E01-27, E01-28

**Tools**:
- MCP: context7 (AWSSDK.SecretsManager)
- Skill: none

**Done when**:
- [ ] `SecretsManagerProvider` implements `ISecretsProvider`; points to LocalStack endpoint when `AWS__ServiceURL` env var is set
- [ ] `GetSecretAsync`: retrieves secret, caches value in `IMemoryCache` with 5-minute absolute expiry
- [ ] Startup validation: on host start, resolves `SecretsProviderOptions` keys (`SesArn`, `KafkaSaslUsername`, `KafkaSaslPassword`) — if any secret is missing, logs `Critical` and throws (fail fast)
- [ ] `ISecretsProvider` registered in DI with `SecretsManagerProvider` implementation
- [ ] Integration test: LocalStack SecretsManager has required secrets → startup succeeds, `GetSecretAsync` returns correct values
- [ ] Integration test: LocalStack SecretsManager missing a required secret → startup throws with `Critical` log entry
- [ ] Integration test: second call within 5 min uses cache (Secrets Manager called only once)
- [ ] Full gate passes

**Tests**: integration
**Gate**: full — `dotnet test`

**Verify**: Start AppHost, remove a secret from LocalStack → API logs `[Critical] Required secret 'SesArn' not found. Startup aborted.` and exits non-zero

**Commit**: `feat(infra): implement SecretsManagerProvider with 5-min cache and fail-fast startup validation`

---

### T13: Create GitHub Actions CI workflow (build + test + coverage) [P]

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

### T17: Configure git-secrets pre-commit hook [P]

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
    ├── T07 (LocalStack container — sequential, Aspire AppHost)
    └── T09 [P with T07] (Kafka container — parallel with T07, different AppHost additions)

  T13 done:
    ├── T14 [P] (Dockerfile + Trivy)
    └── T15 [P] (OWASP step — parallel with T14, different CI steps)

Phase 4 — Integration (all after their respective deps):
  T05 done:
    └── T06 (GlobalExceptionHandler — depends only on T05)

  T07 done:
    └── T08 (LocalStack init script)

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
| T07: LocalStack container in AppHost | 1 AppHost addition | ✅ Granular |
| T08: LocalStack init script | 1 script, 1 concern | ✅ Granular |
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
| E01-03 | T05 | Pending |
| E01-04 | T05 | Pending |
| E01-05 | T06 | Pending |
| E01-06 | T05 | Pending |
| E01-07 | T05 | Pending |
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
