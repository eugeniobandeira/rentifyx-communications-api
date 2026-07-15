# Tasks: Remove Example scaffold, drop EF Core, formalize IOptions<T>

Order matters — each task ends with `dotnet build` (and relevant test run)
staying green before moving to the next. No `[P]` parallel tasks: this is a
single linear deletion chain through the dependency graph, not independent
work.

## T01 — Delete Example endpoints (Api layer)
**What:** Delete `Endpoints/Examples/*`; remove `EXAMPLE` constant from `Tags.cs` (keep `HEALTH`); remove any `MapGroup`/`MapExamples`-style registration call in `AddEndpoints`/`Program.cs` if one references the deleted files.
**Depends on:** none
**Done when:** `dotnet build` succeeds with zero references to `Endpoints.Examples`.

## T02 — Delete Example Application layer
**What:** Delete `Features/Examples/` entirely (handlers, requests, validators, mapper, response). Remove all Example `using`s and the 5 registration lines (validators + handlers) from `ApplicationDependencyInjection.cs`.
**Depends on:** T01
**Done when:** `dotnet build` succeeds; `ApplicationDependencyInjection.Register` only registers Notification-related services.

## T03 — Delete Example Infrastructure layer + EF Core
**What:**
- Delete `Repositories/ExampleRepository.cs`, `Context/AppDbContext.cs`, `Context/Configurations/ExampleConfiguration.cs`, `Context/UnitOfWork.cs`.
- Remove `Microsoft.EntityFrameworkCore.SqlServer` `PackageReference` from `RentifyxCommunications.Infrastructure.csproj` (and `RentifyxCommunications.IoC.csproj` if it has its own direct reference — verify, don't assume).
- In `InfrastructureDependencyInjection.cs`: delete `AddDbContext` method and its call site; delete Example lines from `AddRepositories` (delete the method entirely if nothing else remains in it).
**Depends on:** T02
**Done when:** `dotnet build` succeeds with zero `Microsoft.EntityFrameworkCore*` references anywhere in `02-src`.

## T04 — Delete Example Domain layer + generic repo contracts
**What:**
- Delete `Entities/ExampleEntity.cs`, `Filters/Examples/ExampleFilter.cs`, `Interfaces/Examples/IExampleRepository.cs`, `Constants/ExampleErrorCodes.cs`, `Constants/ValidationConstants.cs`.
- Delete `Interfaces/Common/{IAddRepository,IGetByIdRepository,IUpdateRepository,IDeleteRepository,IGetAllRepository}.cs`, `Interfaces/IUnitOfWork.cs`, `Common/PagedResult.cs`.
- Remove the 5 Example-only keys (`EXAMPLE_NOT_FOUND`, `NAME_REQUIRED`, `NAME_MAX_LENGTH`, `DESCRIPTION_REQUIRED`, `DESCRIPTION_MAX_LENGTH`) from `ValidationMessageResource.resx` and `.pt-BR.resx`; regenerate `.Designer.cs` (do not hand-edit).
**Depends on:** T03 (Infrastructure no longer references `IExampleRepository`/generic interfaces)
**Done when:** `dotnet build` succeeds; grep for `ExampleEntity|IExampleRepository|PagedResult|IAddRepository|IUnitOfWork` across `02-src` returns nothing.

## T05 — appsettings correctness pass
**What:**
- Remove `ConnectionStrings.DefaultConnection` from `appsettings.json` and `appsettings.Test.json`.
- Remove `Microsoft.EntityFrameworkCore`/`Microsoft.EntityFrameworkCore.Database.Command` Serilog overrides from all 3 files.
- Leave the `OpenTelemetry` empty-endpoint keys as-is but add a one-line comment-equivalent note in this task's completion note (appsettings.json has no comment support) — actually verify in Design.md's open question whether these are Aspire env-var-injected; if confirmed, no appsettings change needed here, just confirm.
**Depends on:** T03
**Done when:** All 3 appsettings files parse as valid JSON and contain no dead keys; `dotnet build`/`dotnet test` (integration project) picks up `appsettings.Test.json` without a missing-connection-string error.

## T06 — Typed config records: Cors, RateLimit, OpenApi (no IOptions<T>, no DI registration)
**What:** Create `CorsOptions`, `RateLimitOptions`, `OpenApiDocumentationOptions` records (Api layer, co-located with each Extension or in a shared `Extensions/Options/` folder — match existing folder convention once checked). Each `AddX(services, configuration)` extension method binds its own record once via `configuration.GetSection("X").Get<T>()` and uses it immediately in that same method, replacing today's raw `configuration["..."]`/`configuration.GetSection(...).Get<string[]>()`/`GetValue<int>(...)` calls. **Do not** call `services.Configure<T>()` for these three — nothing else injects them, so there is no DI consumer to register for (per CLAUDE.md's Configuration Binding rule, added 2026-07-15).
**Depends on:** T05 (appsettings sections must exist first — Cors/RateLimit/OpenApi already have real sections, just confirm at task time)
**Done when:** All 3 extensions read from a bound record instead of raw indexers; `dotnet build`+relevant tests pass; manual `dotnet run` confirms CORS/rate-limit/OpenAPI still behave identically (same origins, same limits, same contact info).

## T07 — IOptions<T>: Kafka (AWS is out of scope — see below)
**What:**
- **AWS: no change.** `configuration.GetAWSOptions()` (AWS SDK) already binds the `"AWS"` section for `AddDefaultAWSOptions`; the `Profile` fail-fast check in `InfrastructureDependencyInjection.AddAwsOptions` keeps reading `configuration["AWS:Profile"]` directly. Confirmed out of scope during Specify (2026-07-15) — do not create an `AwsOptions` record.
- Create `KafkaOptions` (`ConsumerGroupId` only), bind via `services.Configure<KafkaOptions>(configuration.GetSection("Kafka"))`. Update `KafkaConsumerFactory`, `KafkaProducerFactory`, `NotificationRequestedConsumer` to take `IOptions<KafkaOptions>` **in addition to** `IConfiguration` (kept only for the `configuration.GetConnectionString("kafka")` Aspire lookup — documented exception, see design.md). Do not try to eliminate `IConfiguration` entirely from these three; the split is intentional.
**Depends on:** T06
**Done when:** `dotnet build` succeeds; Kafka consumer/producer still connect against the Aspire-provided broker in a manual `dotnet run` smoke check.

## T08 — IOptions<T>: Resilience, Reconciliation, SecretsProvider
**What:**
- `InfrastructureDependencyInjection`: replace `configuration.GetSection("Resilience").Get<ResilienceOptions>() ?? new()` + `AddSingleton(instance)` with `services.Configure<ResilienceOptions>(configuration.GetSection("Resilience"))`. Update `ResilienceStartupValidator` and the `ResiliencePipelineFactory.Create(...)` call site to take `IOptions<ResilienceOptions>` and unwrap `.Value`.
- Same swap for `ReconciliationOptions` → wherever it's consumed.
- Add a `SecretsProvider` config section (3 keys, matching today's defaults) to `appsettings.json`; register `services.Configure<SecretsProviderOptions>(configuration.GetSection("SecretsProvider"))`; update `SecretsManagerProvider`'s constructor from the bare record to `IOptions<SecretsProviderOptions>`.
**Depends on:** T07
**Done when:** `dotnet build` succeeds; `ResilienceStartupValidatorTests`/`SecretsManagerProviderTests` (existing unit/integration tests) still pass with the new constructor shape — update test setup to wrap the record in `Options.Create(...)` (Microsoft.Extensions.Options's test helper) rather than passing the bare record.

## T09 — Delete Example tests across all test projects
**What:** Delete `01-Common/Builders/ExampleBuilder.cs`, `02-Validators/Features/Examples/*`, `03-Handlers/Features/Examples/*`, `04-Repositories/Features/Examples/ExampleRepositoryTests.cs`, `05-Integration/Api/Examples/ExampleEndpointTests.cs`. Delete `CustomWebApplicationFactory` if grep confirms it has no other consumer after this deletion. Check `00-Domain` and `06-Api` for any stray Example reference missed by the initial survey.
**Depends on:** T04, T08 (test doubles for Options-based constructors need the new shape)
**Done when:** `dotnet test` (excluding `Category=Integration`/`Category=LoadTest` per existing CI filter) passes with zero Example-related test files remaining; test count drops by exactly the number of deleted `[Fact]`/`[Theory]` methods (sanity check, not a hard gate).

## T10 — Full verification + STATE.md sync
**What:** Full `dotnet build` + `dotnet test` (both filtered and, locally, the Integration category if AWS/Kafka are reachable) + a `dotnet run` smoke check (API boots, `/scalar` loads in dev, a manual Kafka message still dispatches end-to-end). Update `.specs/project/STATE.md`: mark this cleanup done, remove the now-resolved `CustomWebApplicationFactory` EF-InMemory todo and the "Examples scaffold doesn't match architecture" todo (both fully resolved by this work), note the coverage-gate percentage change (deleting a large chunk of low-value Example tests will lower the numerator but also the denominator — check whether the ~5.6% coverage figure moves and whether that's now closer to or further from the 80% gate).
**Depends on:** T09
**Done when:** CI-equivalent local run is green; STATE.md accurately reflects the new state; no dangling references to `Example`/`EF Core`/`SqlServer` anywhere in `02-src`/`03-tests` (grep-verified).

---

## Traceability

| Task | Requirements covered |
|---|---|
| T01 | R1.1 |
| T02 | R1.2, R1.8 (Application half) |
| T03 | R1.6, R1.8 (Infrastructure half), R2.1, R2.2 |
| T04 | R1.3, R1.4, R1.5, R3.1, R3.2, R3.3 |
| T05 | R2.3, R5.1, R5.3, R5.4 |
| T06 | R4.1, R4.2, R4.3, R4.8 (Cors/RateLimit/OpenApi) |
| T07 | R4.4 (AWS confirmed out of scope), R4.5, R4.8 (Kafka), R5.2 (Kafka section) |
| T08 | R4.6, R4.7, R5.2 (Resilience/Reconciliation/SecretsProvider sections) |
| T09 | R1.7, R2.4 |
| T10 | (verification, no new requirement) |
