# Design: Remove Example scaffold, drop EF Core, formalize IOptions<T>

## Architecture

No new components — this is a deletion + a config-binding pattern swap.
Layer order for Execute: **Domain → Application → Infrastructure → IoC → Api → Tests**,
i.e. delete from the innermost layer outward so each step's build stays green
(deleting `ExampleEntity` first would leave `IExampleRepository`/`ExampleRepository`
referencing a type that no longer exists — bad order — so this plan deletes
top-down through actual compile dependencies instead, see task order below).

Correct dependency-safe order is actually **outside-in for deletion**:
Api endpoints (nothing depends on them) → Application handlers (only Api
depends on them) → Infrastructure repositories (only IoC/Application-via-DI
depend on them) → Domain entities/interfaces (everything else depends on
these, delete last) → IoC wiring cleanup → csproj/appsettings → Tests.

## Config binding pattern

Two different treatments, chosen per CLAUDE.md's new "Configuration Binding"
rule — the deciding question is: **does the DI container construct a class
that needs this value via constructor injection, or is it a one-shot read
inside a startup extension method that already has `IConfiguration` in scope?**

**Group A — plain typed record, bound once, no `IOptions<T>`/DI registration:**
`Cors`, `RateLimit`, `OpenApi`. Each `AddX(services, configuration)` extension
method binds its own record with `configuration.GetSection("X").Get<T>()`
and uses it immediately in that same method. Nothing else ever needs these
values, so there is no DI consumer to inject them into — registering them
with `services.Configure<T>()` would be dead ceremony.

`AWS` gets no custom record at all: `configuration.GetAWSOptions()` (AWS
SDK's own extension) already reads the `"AWS"` section by convention when
building `AddDefaultAWSOptions`. The only other AWS-related read is the
`Profile` fail-fast check, which keeps using `configuration["AWS:Profile"]`
directly — wrapping one string read in a record just to satisfy a pattern
isn't worth it, and would duplicate binding logic the SDK already owns.

**Group B — real `IOptions<T>`, registered via `services.Configure<T>()`:**
`KafkaOptions`, `ResilienceOptions` (existing), `ReconciliationOptions`
(existing), `SecretsProviderOptions` (existing, Application layer). All four
back a class the DI container **constructs** (a registered
Singleton/Scoped/HostedService) and that takes the options via constructor
injection — this is exactly the case `IOptions<T>` is for.

| Options type | Section | Consumed by | Notes |
|---|---|---|---|
| `KafkaOptions` | `Kafka` | `KafkaConsumerFactory`, `KafkaProducerFactory`, `NotificationRequestedConsumer` | Only `ConsumerGroupId`. `BootstrapServers` stays a `GetConnectionString("kafka")` call inside each factory — Aspire service-discovery value, not a static setting, so it is **not** part of `KafkaOptions`. These classes keep `IConfiguration` *in addition to* `IOptions<KafkaOptions>`, purely for that one Aspire lookup — document this split inline. |
| `ResilienceOptions` (existing) | `Resilience` | `ResilienceStartupValidator`, `ResiliencePipelineFactory` call site | Switch registration from `AddSingleton(instance)` to `Configure<T>()`; consumers take `IOptions<ResilienceOptions>` instead of the bare record. |
| `ReconciliationOptions` (existing) | `Reconciliation` | wherever it's consumed (`ReconciliationHostedService` or its options holder) | Same switch as above. |
| `SecretsProviderOptions` (existing, Application layer) | `SecretsProvider` | `SecretsManagerProvider` | Currently constructed with hardcoded defaults and never read from config at all — this is the one behavior change: it becomes overridable via config, still defaulting to today's 3 key names. |

All Group B records stay plain `sealed record`s with default values matching
today's hardcoded fallbacks (per CLAUDE.md: multi-line params when >1 field).
`.Value` is read once at construction for classes that are themselves
`Singleton`/`Scoped` and don't need live-reload — none of these need
`IOptionsMonitor<T>`, config doesn't change without a redeploy.

`ResiliencePipelineFactory.Create` and any other static factory that takes
the raw record as a parameter keep taking the plain record type (not
`IOptions<T>`) — `IOptions<T>` is a DI-resolution concern, not something a
pure static factory method should know about. The DI registration lambda
unwraps `.Value` before calling the factory, e.g.:

```csharp
services.AddSingleton(sp =>
    ResiliencePipelineFactory.Create(sp.GetRequiredService<IOptions<ResilienceOptions>>().Value));
```

## File inventory (delete list)

**Api layer**
- `Endpoints/Examples/` (Create.cs, Delete.cs, GetAll.cs, GetById.cs, Update.cs)
- `Tags.cs` → remove `EXAMPLE` constant only, keep file (still used for `HEALTH`)

**Application layer**
- `Features/Examples/` (entire folder: ExampleResponse.cs, Handlers/**, Mapper/ExampleMapper.cs)

**Domain layer**
- `Entities/ExampleEntity.cs`
- `Filters/Examples/ExampleFilter.cs`
- `Interfaces/Examples/IExampleRepository.cs`
- `Interfaces/Common/` (all 5 generic repo interfaces) + `Interfaces/IUnitOfWork.cs`
- `Common/PagedResult.cs`
- `Constants/ExampleErrorCodes.cs`
- `Constants/ValidationConstants.cs` — delete entirely (only contained `ExampleRules`)
- `MessageResource/ValidationMessageResource.resx` + `.pt-BR.resx` — remove 5 Example keys only (regenerate `.Designer.cs` via the resx tooling, do not hand-edit the generated file)

**Infrastructure layer**
- `Context/AppDbContext.cs`
- `Context/Configurations/ExampleConfiguration.cs`
- `Context/UnitOfWork.cs`
- `Repositories/ExampleRepository.cs`
- `RentifyxCommunications.Infrastructure.csproj`: remove `Microsoft.EntityFrameworkCore.SqlServer` `PackageReference`

**IoC layer**
- `ApplicationDependencyInjection.cs`: remove all Example `using`s + registrations (validators, handlers)
- `InfrastructureDependencyInjection.cs`: remove `AddDbContext` method entirely + its call in `Register`; remove Example lines from `AddRepositories` (delete the whole method if nothing else populates it — confirm at task time)
- `RentifyxCommunications.IoC.csproj`: remove EF Core package reference if present only for this

**Tests** (one test project per concern, per CLAUDE.md structure)
- `00-Domain`: none (Example had no domain unit tests beyond what's listed elsewhere — verify at task time)
- `01-Common`: `Builders/ExampleBuilder.cs`
- `02-Validators`: `Features/Examples/CreateExampleValidatorTests.cs`, `UpdateExampleValidatorTests.cs`
- `03-Handlers`: `Features/Examples/` (all 4 handler test files)
- `04-Repositories`: `Features/Examples/ExampleRepositoryTests.cs`
- `05-Integration`: `Api/Examples/ExampleEndpointTests.cs`; `CustomWebApplicationFactory` (delete if this was its only consumer — grep-confirm at task time)
- `06-Api`: none identified — verify at task time

**appsettings**
- `appsettings.json`: remove `ConnectionStrings.DefaultConnection`, remove `Microsoft.EntityFrameworkCore` Serilog override; add `Cors`(already present)/`RateLimit`(already present)/`OpenApi`(already present)/`AWS`/`Kafka`/`Resilience`/`Reconciliation`/`SecretsProvider` sections as needed so nothing relies on a silent C# default
- `appsettings.Development.json`: remove `Microsoft.EntityFrameworkCore.Database.Command` Serilog override; add `AWS` section (Profile is empty string today — stays empty, developer sets via user-secrets per existing convention, do not put a real profile name in a committed file)
- `appsettings.Test.json`: remove `ConnectionStrings.DefaultConnection`, remove `Microsoft.EntityFrameworkCore` Serilog override

## Risks / things to verify during Execute, not assume now

- Confirm via `dotnet build` after each layer's deletion pass — don't batch all 5 layers then discover a break, per CLAUDE.md's own lesson from F-09 Execute (layering violations were "caught by actually building after each new file").
- Confirm no test project outside the ones listed references `AppDbContext`/`UnitOfWork`/`PagedResult`/the generic repo interfaces before deleting (grep already run once during Specify — re-verify at Execute time since this doc may go stale).
- `ValidationMessageResource.Designer.cs` is machine-generated from the `.resx` — editing the `.resx` requires the resx custom tool to regenerate the designer file (Visual Studio does this on save; if running headless, may need `dotnet build` or an explicit resgen step — verify which applies in this repo before hand-editing the `.Designer.cs`).
- `RentifyxCommunications.IoC.csproj`'s EF Core reference — check if it's a direct `PackageReference` or only transitively visible from a `ProjectReference` to Infrastructure; only remove a direct reference.
