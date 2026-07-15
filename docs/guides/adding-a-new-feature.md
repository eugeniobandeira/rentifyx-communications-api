# Guide: Adding a New Feature

Use the `Notifications` feature (`Features/Notifications/Handlers/Dispatch/`) as the reference
implementation — it's the one real, fully-built feature in this codebase and follows every
convention in the root `CLAUDE.md`. Read that file first; this guide just walks the layers in
order.

## 1. Domain

- Add `<Feature>Entity.cs` under `03-Domain/.../Entities/` if the feature introduces a new
  aggregate with identity (see `NotificationEntity`). Value objects, enums, and domain events
  do **not** take an `Entity`/`VO`/`Event` suffix (see `EmailAddress`, `Channel`,
  `NotificationDispatched`).
- Add a repository contract under `03-Domain/.../Interfaces/<Feature>/` (its own subfolder —
  never loose directly under `Interfaces/`), e.g. `INotificationRepository`. Shape the
  interface around what the feature actually needs (`SaveIfNotExistsAsync`, `GetByIdAsync`,
  ...) — there are no generic `IAddRepository<T>`/`IGetByIdRepository<T>`/etc. contracts to
  compose against; those were removed as unused template scaffolding (2026-07-15) and should
  only be recreated if a second feature genuinely needs the same generic shape.
- Add error codes to `Domain/Constants/<Feature>ErrorCodes.cs` if the feature needs named
  business-error codes (see `NotificationErrorCodes`).
- Add resource keys to `ValidationMessageResource.resx` and `ValidationMessageResource.pt-BR.resx`
  for any FluentValidation message that needs localization. The `.Designer.cs` is not
  regenerated automatically by `dotnet build` in this repo — edit it by hand to match the
  `.resx` additions (add the corresponding `public static string KEY { get; }` property).

## 2. Application

Create one subfolder per operation under `02-Application/.../Features/<Feature>/Handlers/<Operation>/`:

```
Features/<Feature>/
  Handlers/
    <Operation>/
      <Operation><Feature>Handler.cs      # the only file loose at this level
      Request/<Operation><Feature>Request.cs
      Validator/<Operation><Feature>Validator.cs
      Response/<Operation><Feature>Response.cs   # NOT "...Result" or "...Outcome"
```

- The handler implements `IHandler<TRequest, TResponse>` and returns `ErrorOr<TResponse>` —
  never throws for expected/business-rule failures (see `DispatchNotificationHandler`).
- If multiple entry points (e.g. several Kafka consumers) share the same
  deserialize → handle → route logic, extract it into one shared processor class
  (see `NotificationDispatchProcessor`) instead of duplicating it per consumer.

## 3. Infrastructure

- Add `<Feature>Repository.cs` under `05-Infrastructure/.../Repositories/<Feature>/`
  implementing `I<Feature>Repository` (see `DynamoDbNotificationRepository`). If the feature
  persists to DynamoDB, follow the existing single-table design — extend
  `Domain/Constants/NotificationTableSchema.cs` rather than inlining new `"GSIn"`/`"GSInPK"`
  string literals, and use a mapper class (see `NotificationItemMapper`) instead of inline
  `ToItem`/`FromItem` methods on the repository itself.
- This project has no relational database (EF Core/SQL Server was removed 2026-07-15 as unused
  template scaffolding) — all persistence is DynamoDB. Don't reintroduce EF Core unless a real,
  confirmed need for a relational store arises.

## 4. Configuration (IOptions<T> vs. plain config read)

See CLAUDE.md's "Configuration Binding" section for the full rule. In short: only use
`services.Configure<T>()` + constructor-injected `IOptions<T>` for a record consumed by a class
the DI container constructs (a registered Singleton/Scoped/HostedService). For a one-shot
config read inside a startup `AddX(services, configuration)` extension method, just bind a
plain typed record with `configuration.GetSection("X").Get<T>()` — no `IOptions<T>` ceremony.
An Options record's properties should have **no hardcoded fallback values that duplicate real
appsettings.json entries** (see `KafkaOptions`, `DynamoDbOptions`, `SecretsProviderOptions`) —
if config is the source of truth, don't also bake the same value into the C# default.

## 5. IoC

- Register the handler/validator in `ApplicationDependencyInjection.cs`.
- Register the repository (and any `Configure<T>()` config binding) in
  `InfrastructureDependencyInjection.cs`.

## 6. Api

- Add one file per operation under `01-Api/.../Endpoints/<Feature>/` implementing `IEndpoint`
  (endpoints are discovered by reflection in `EndpointExtensions.AddEndpoints` — no manual
  registration needed).
- Add the feature's tag constant to `Endpoints/Tags.cs`.

## 7. Tests

| Project | What to add |
|---|---|
| Tests.Common | `<Feature>Builder.cs` (Bogus-based test data builder) |
| Tests.Validators | `<Operation><Feature>ValidatorTests.cs` |
| Tests.Handlers | One file per handler operation |
| Tests.Repositories | `<Feature>RepositoryTests.cs` (unit-level, mocked client) |
| Tests.Integration | `<Feature>RepositoryTests.cs` against a real LocalStack container
  (see `DynamoDbNotificationRepositoryTests`), tagged `[Trait("Category", "Integration")]` |
| Tests.Api | Consumer/middleware tests if the feature adds an `IHostedService` or middleware |

Run `dotnet test --filter "Category!=Integration&Category!=LoadTest"` for the fast unit suite
(matches the CI filter); drop the filter (with Docker running) to also exercise
Testcontainers-backed integration tests.
