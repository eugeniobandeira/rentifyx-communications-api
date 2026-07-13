# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Architecture

Clean Architecture, 5 layers: `01-Api`, `02-Application`, `03-Domain`, `04-IoC`, `05-Infrastructure` (see `02-src/`), plus `01-aspire/` (AppHost + ServiceDefaults) and `03-tests/` (one test project per concern: `00-Domain`, `01-Common`, `02-Validators`, `03-Handlers`, `04-Repositories`, `05-Integration`, `06-Api`).

## Naming Conventions

- **Entities get an `Entity` suffix**: `ExampleEntity`, `NotificationEntity`. Applies to any Domain type with identity that a repository persists — not to value objects, enums, or events.
- **Value objects, enums, and domain events do NOT take a suffix** describing their kind (no `...VO`, `...Event`): `EmailAddress`, `Channel`, `NotificationDispatched`.
- **Interfaces are prefixed `I`**, as usual: `INotificationRepository`, `IEmailSender`.

## File & Folder Organization

**Interfaces are always segmented into their own folder by domain concept — never declared loose alongside implementation classes or dumped into one flat folder.**

```
Domain/Interfaces/
├── Common/              # generic, cross-entity contracts (IAddRepository<T>, IUnitOfWork, ...)
├── Examples/             # IExampleRepository
└── Notifications/        # INotificationRepository, IConsentRepository, ITemplateRenderer, IEmailSender
```

Do not place an interface directly under `Interfaces/` (loose) when it belongs to a specific domain concept — give it a subfolder named after that concept, matching the `Examples/` precedent. The same rule applies to `Application/`: validators, requests, and handlers each get their own subfolder under `Features/{Feature}/Handlers/{Action}/`.

## Constructors

**Prefer primary constructors (C# 12+)** for classes whose constructor only assigns injected dependencies to fields/properties — do not hand-write a constructor body that just does `_x = x;` for every parameter.

```csharp
// Preferred
public sealed class SecretsManagerProvider(
    IAmazonSecretsManager client,
    IMemoryCache cache) : ISecretsProvider
{
    public async Task<string> GetSecretAsync(string name) => ...;  // use `client`/`cache` directly
}

// Avoid
public sealed class SecretsManagerProvider : ISecretsProvider
{
    private readonly IAmazonSecretsManager _client;
    private readonly IMemoryCache _cache;

    public SecretsManagerProvider(IAmazonSecretsManager client, IMemoryCache cache)
    {
        _client = client;
        _cache = cache;
    }
}
```

**Exception:** aggregates/entities with invariants to enforce at construction time (e.g. `NotificationEntity.Create(...)` validating and returning `ErrorOr<T>`) keep a `private` parameterless-or-full constructor plus a `static Create(...)` factory — primary constructors don't fit a type that must validate before allowing construction to succeed.

## Method Bodies

**Constructors only: use a block body (`{ }`), not an expression body (`=>`)** — even a one-line assignment. This applies specifically to explicit (non-primary) constructors; it does not apply to regular methods, factory methods, property getters, or primary constructor parameter lists, all of which may freely use expression bodies where that reads well.

```csharp
// Preferred (constructor)
private EmailAddress(string value)
{
    Value = value;
}

// Avoid (constructor)
private EmailAddress(string value) => Value = value;

// Fine either way (regular/factory method, not a constructor)
public void Deactivate() => IsActive = false;
public static ConsentDecision NoRecordFound() => new(isSuppressed: false);
```

## Code Style

**Multi-line parameter lists for records, primary constructors, and factory methods with more than one parameter.** Put each parameter on its own line, rather than a single long line.

```csharp
// Preferred
public sealed record NotificationDelivered(
    Guid NotificationId,
    Guid CorrelationId,
    DateTime OccurredAt) : IDomainEvent;

public sealed class SecretsManagerProvider(
    IAmazonSecretsManager client,
    IMemoryCache cache) : ISecretsProvider
{
}

// Avoid
public sealed record NotificationDelivered(Guid NotificationId, Guid CorrelationId, DateTime OccurredAt) : IDomainEvent;

public sealed class SecretsManagerProvider(IAmazonSecretsManager client, IMemoryCache cache) : ISecretsProvider
{
}
```

Single-parameter records/constructors can stay on one line.

## Async Naming

**Every async method gets an `Async` suffix** — `RenderAsync`, `SendAsync`, `FindAsync`, `SaveIfNotExistsAsync`. No exceptions for interface members either: if a method returns `Task`/`Task<T>` (or `ValueTask`/`ValueTask<T>`), its name ends in `Async`, whether declared on an interface or its implementation.

## Error Handling

- Application/Domain methods that can fail on **expected, runtime business outcomes** (validation, business rules, external call failures) return `ErrorOr<T>` — never throw for these cases.
- Constructors/guard clauses that protect against **programmer error** (a null that should never happen given the caller's contract) may still throw (`ArgumentException.ThrowIfNullOrWhiteSpace`, etc.) — this is a narrower case than the general `ErrorOr` rule above and should stay rare.

## No Magic Numbers or Strings

Always use named constants instead of inline literals repeated or meaningful across the codebase — no bare numbers/strings whose meaning isn't obvious at the call site.

- Error codes, validation limits, and repeated string keys go in `Domain/Constants/` (e.g. `NotificationErrorCodes.InvalidPayload`, `ValidationConstants.ExampleRules.NameMaxLength`), never inlined as `"Notification.InvalidPayload"` at each call site.
- This applies to Kafka topic names, config keys, and any other value referenced from more than one place.

## Enum Persistence

**Never persist an enum as its underlying numeric value.** Always store/serialize the string name (`"Sent"`, `"Email"`), never the `int` (`3`, `0`) — a number in a database record with no enum definition next to it is meaningless on its own.

- Applies to `Channel` and `NotificationStatus` once E-04 implements `DynamoDbNotificationRepository` — use `.ToString()` / an explicit converter, never the default numeric marshalling.
- Tracked as an explicit todo in `.specs/project/STATE.md` for when E-04 lands.
