# Spec: Remove Example scaffold, drop EF Core, formalize IOptions<T>

**Status:** Planned (not started)
**Created:** 2026-07-15
**Type:** Tech debt cleanup (cross-cutting, all 5 layers + tests)

## Context

The `dotnet new clean-arch` template scaffold shipped an `Example` CRUD feature
(EF Core + SQL Server) across all 5 layers plus 6 test projects. It was never
real product scope — `.specs/project/STATE.md` already flagged it as
"template boilerplate that predates the DynamoDB decision" and expected it to
be replaced, not extended, once E-04 landed. E-04 is now fully merged
(F-07/F-08/F-09), so nothing in the product depends on Example or EF Core
anymore. Separately, several config reads use raw `IConfiguration` string
indexers/`GetSection().Get<T>()` scattered across Extension methods and DI
registration instead of the idiomatic `services.Configure<T>()` +
constructor-injected `IOptions<T>` pattern — a source of typo/miskey risk
that only surfaces at runtime.

User decisions (confirmed 2026-07-15, see AskUserQuestion record below):

1. **Delete** the generic repository contracts (`IAddRepository<T>`,
   `IGetByIdRepository<T>`, `IUpdateRepository<T>`, `IDeleteRepository<T>`,
   `IGetAllRepository<T>`), `PagedResult<T>`, and `IUnitOfWork` — nothing
   else implements them today; recreate them shaped to the real need if/when
   a future CRUD entity actually requires them.
2. **Remove EF Core / SQL Server entirely** — `Microsoft.EntityFrameworkCore.SqlServer`
   package, `AppDbContext`, `UnitOfWork`, `ExampleConfiguration`, and the
   `ConnectionStrings:DefaultConnection` key from all 3 appsettings files.
   This also resolves the standing `CustomWebApplicationFactory` EF-InMemory
   todo in STATE.md — moot once there's no EF Core at all.
3. **IOptions<T> migration is scoped to where it actually pays off** (revised
   2026-07-15 after discussion — see CLAUDE.md's new "Configuration Binding"
   section): only classes the DI container *constructs* and that need config
   via constructor injection get `IOptions<T>` — `KafkaOptions` (consumed by
   `KafkaConsumerFactory`/`KafkaProducerFactory`/`NotificationRequestedConsumer`)
   and the existing `ResilienceOptions`/`ReconciliationOptions`/`SecretsProviderOptions`
   (today manually bound or hardcoded) via real `services.Configure<T>()` +
   `IOptions<T>` injection. `Cors`/`RateLimit`/`OpenApi` are read once inside
   their own startup `AddX` extension method and never injected elsewhere —
   they get a plain typed record bound with `configuration.GetSection("X").Get<T>()`,
   no `IOptions<T>`/DI registration ceremony. `AWS` is not wrapped in a
   custom options record at all — `configuration.GetAWSOptions()` (AWS SDK)
   already binds the `"AWS"` section by convention; only the `Profile`
   fail-fast check needs a value, and it keeps reading `configuration["AWS:Profile"]` directly.

## Requirements

### R1 — Remove Example feature (all layers)
- R1.1: Delete `Endpoints/Examples/*`, `Endpoints/Tags.cs`'s `EXAMPLE` tag (keep `HEALTH`).
- R1.2: Delete `Application/Features/Examples/*` (handlers, requests, validators, mapper, response).
- R1.3: Delete `Domain/Entities/ExampleEntity.cs`, `Domain/Filters/Examples/ExampleFilter.cs`,
  `Domain/Interfaces/Examples/IExampleRepository.cs`, `Domain/Constants/ExampleErrorCodes.cs`.
- R1.4: Delete `ValidationConstants.ExampleRules` nested class (keep the outer
  `ValidationConstants` class shell only if another rule set needs it — otherwise delete the file).
- R1.5: Delete Example-only resx entries (`EXAMPLE_NOT_FOUND`, `NAME_REQUIRED`,
  `NAME_MAX_LENGTH`, `DESCRIPTION_REQUIRED`, `DESCRIPTION_MAX_LENGTH`) from
  `ValidationMessageResource.resx`/`.pt-BR.resx` — **keep** the Notification-specific
  keys (`CHANNEL_INVALID`, `CORRELATION_ID_REQUIRED`, `PAYLOAD_REQUIRED`,
  `RECIPIENT_EMAIL_REQUIRED`, `RECIPIENT_ID_REQUIRED`, `TEMPLATE_ID_REQUIRED`).
- R1.6: Delete `Infrastructure/Repositories/ExampleRepository.cs`.
- R1.7: Delete all Example-only test files (see Design doc file inventory) —
  `ExampleBuilder`, `Create/Delete/GetAll/GetByIdExampleHandlerTests`,
  `CreateExampleValidatorTests`, `UpdateExampleValidatorTests`,
  `ExampleRepositoryTests`, `ExampleEndpointTests`.
- R1.8: Remove all Example wiring from `ApplicationDependencyInjection` and
  `InfrastructureDependencyInjection` (`AddRepositories`'s Example lines).

### R2 — Remove EF Core / SQL Server
- R2.1: Delete `AppDbContext`, `Context/Configurations/ExampleConfiguration.cs`, `UnitOfWork`, `IUnitOfWork`.
- R2.2: Remove `Microsoft.EntityFrameworkCore.SqlServer` (and any transitive
  EF Core design/tools packages only referenced for this) from
  `RentifyxCommunications.Infrastructure.csproj` / `RentifyxCommunications.IoC.csproj`.
- R2.3: Remove `ConnectionStrings.DefaultConnection` from `appsettings.json`,
  `appsettings.Test.json`; remove `Microsoft.EntityFrameworkCore`/
  `Microsoft.EntityFrameworkCore.Database.Command` Serilog overrides from all
  3 appsettings files (nothing emits those categories once EF is gone).
- R2.4: Fix or delete `CustomWebApplicationFactory` — since its only consumer
  (`ExampleEndpointTests`) is deleted by R1.7, delete the factory too unless
  something else in `03-Integration`/`06-Api` test projects references it.

### R3 — Generic repository/unit-of-work contracts
- R3.1: Delete `Domain/Interfaces/Common/{IAddRepository,IGetByIdRepository,IUpdateRepository,IDeleteRepository,IGetAllRepository}.cs`.
- R3.2: Delete `Domain/Common/PagedResult.cs`.
- R3.3: Confirm no other type (Notification-side) references any of R3.1/R3.2 before deleting (grep-verified in Design).

### R4 — Configuration binding cleanup (IOptions<T> where it pays off, typed records elsewhere)
- R4.1: `CorsOptions` (`AllowedOrigins`) — plain record, bound once inside
  `CorsExtension.AddCorsPolicy` via `configuration.GetSection("Cors").Get<T>()`.
  **Not** registered with `services.Configure<T>()` — nothing else injects it.
- R4.2: `RateLimitOptions` (`PermitLimit`, `WindowSeconds`, `QueueLimit`) — same
  treatment, bound once inside `RateLimitExtension.AddRateLimiting`.
- R4.3: `OpenApiDocumentationOptions` (`ContactName`, `ContactUrl`) — same
  treatment, bound once inside `OpenApiExtensions.AddOpenApiDocumentation`.
  Named to avoid colliding with `Microsoft.OpenApi`'s own types.
- R4.4: **AWS is not wrapped in a custom options record.**
  `configuration.GetAWSOptions()` (AWS SDK's own extension) already binds the
  `"AWS"` section by convention for `AddDefaultAWSOptions`. The `Profile`
  fail-fast check in `InfrastructureDependencyInjection.AddAwsOptions` keeps
  reading `configuration["AWS:Profile"]` directly — no change needed here
  beyond removing it from migration scope.
- R4.5: `KafkaOptions` (`ConsumerGroupId`) — **this one uses real `IOptions<T>`**
  since `KafkaConsumerFactory`, `KafkaProducerFactory`, and
  `NotificationRequestedConsumer` are all DI-constructed (Singleton/HostedService)
  and take it via constructor injection. `BootstrapServers` stays resolved via
  `configuration.GetConnectionString("kafka")` at factory construction time
  (Aspire service-discovery convention, not a static appsetting) — document
  this exception explicitly so a future reader doesn't "fix" it to match `ConsumerGroupId`.
- R4.6: `ResilienceOptions`/`ReconciliationOptions` — **real `IOptions<T>`**,
  switching from manual `configuration.GetSection(...).Get<T>() ?? new T()` +
  `services.AddSingleton(instance)` to `services.Configure<T>(configuration.GetSection(...))`
  + `IOptions<T>` injected into `ResilienceStartupValidator`,
  `ResiliencePipelineFactory` call site, `ReconciliationHostedService`
  (wherever `ReconciliationOptions` is consumed) — these are all DI-constructed services.
- R4.7: `SecretsProviderOptions` — **real `IOptions<T>`**. Currently
  `new SecretsProviderOptions()` with hardcoded defaults never bound from
  config (per STATE.md, confirmed intentional at the time). Add a
  `SecretsProvider` config section with the same 3 keys as defaults, bind via
  `services.Configure<T>()` + `IOptions<T>` injected into
  `SecretsManagerProvider` (DI-constructed Singleton), so an environment can
  override the Secrets Manager key names without a code change — while
  keeping today's values as defaults.
- R4.8: `CorsExtension`, `RateLimitExtension`, `OpenApiExtensions` keep taking
  `IConfiguration` (they bind their own typed record internally per R4.1-R4.3
  — no constructor injection involved, these are static extension methods,
  not DI-constructed classes). `KafkaConsumerFactory`, `KafkaProducerFactory`,
  `NotificationRequestedConsumer` switch from `IConfiguration` to
  `IOptions<KafkaOptions>` (plus keeping `IConfiguration` only for the
  `GetConnectionString("kafka")` Aspire lookup per R4.5).
  `InfrastructureDependencyInjection.AddAwsOptions` is unchanged (R4.4).

### R5 — appsettings correctness review
- R5.1: Remove dead keys per R2.3.
- R5.2: Add the `Kafka`, `Resilience`, `Reconciliation`, `SecretsProvider`
  sections (the actual `IOptions<T>` ones, per R4) to `appsettings.json` (or
  `appsettings.Development.json` where environment-specific) so every bound
  `IOptions<T>` has a real, correct, visible source — no more "works because
  a hardcoded C# default happens to match." `Cors`/`RateLimit`/`OpenApi`
  already have real sections (confirmed present); `AWS` is untouched per R4.4.
- R5.3: Verify `appsettings.Test.json` still has what `03-tests` integration
  tests actually need after Example/EF removal (likely just Serilog/Cors/OpenApi —
  no `DefaultConnection` needed anymore).
- R5.4: `OpenTelemetry` section in `appsettings.json` has empty
  `OTEL_EXPORTER_OTLP_ENDPOINT`/`HEADERS` — confirm this is intentional
  (Aspire injects these via env vars in dev, real values come from the
  deployment environment) rather than a bug; document if so, don't silently "fix."

## Out of scope
- No behavior change to Notification dispatch, resilience, or reliability logic.
- No new appsettings values invented beyond what's needed to back existing `IOptions<T>` records with real config (no speculative new features).
- Not touching `SecretsManagerProvider`'s runtime Secrets Manager calls — only how it discovers *which keys* to fetch.
