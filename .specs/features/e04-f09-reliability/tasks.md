# E-04 · F-09 — Reliability Tasks

**Design**: `.specs/features/e04-f09-reliability/design.md`
**Status**: Approved

---

## Test Strategy (reusing gate commands from E-01/E-03/F-07/F-08)

| Gate | Command | When |
| --- | --- | --- |
| **build** | `dotnet build --no-incremental` | Constants, enums, interfaces, options records — no branching logic to unit test |
| **quick** | `dotnet test --filter "Category!=Integration"` | Unit tests (classifier, router, processor, consumers — all mockable, no real Kafka/DynamoDB) |
| **full** | `dotnet test` | Unit + integration (LocalStack — GSI3/reconciliation query) |

## Test Coverage Matrix

| Code Layer | Test Type Required | Parallel-Safe |
| --- | --- | --- |
| `SesErrorCodes`/`TemplateErrorCodes` extraction (T01) | none (mechanical; existing tests must still pass) | Yes |
| `RetryTopicChain` (T02) | unit | Yes |
| `FailureClassification` enum (T03) | none | Yes |
| `Api/Consumers` → `Api/Messaging` rename (T04) | none (mechanical; existing tests must still pass) | Yes |
| `RetryContext` (T05) | none | Yes |
| `FailureClassifier` (T06) | unit | Yes |
| `IFailureRouter` interface (T07) | none | Yes |
| `IKafkaProducerFactory`/`KafkaProducerFactory` (T08) | none (mirrors untested `KafkaConsumerFactory`) | Yes |
| GSI3 + `GetStuckDispatchingAsync` (T14) | integration | **No** — shared LocalStack container, same constraint as F-07's T10/T11 |
| `ReconciliationOptions` (T15) | none | Yes |
| `KafkaFailureRouter` (T09) | unit | Yes |
| `NotificationDispatchProcessor` (T10) | unit | Yes |
| `NotificationRequestedConsumer` extension (T11) | unit | Yes |
| `RetryTopicConsumer` (T12) | unit | Yes |
| `DlqObserverHostedService` (T13) | unit | Yes |
| `ReconciliationHostedService` (T16) | unit | Yes |
| DI wiring (T17) | none (build-only, exercised transitively) | Yes |
| OTEL metrics (T18) | unit if feasible, else manual (Design flagged this needs fresh research) | Yes |
| Docs (T19) | none | Yes |

**Parallelism Assessment**: All unit tests are pure (Moq/fakes) — parallel-safe, matching F-08's pattern. Only T14 touches LocalStack and is sequential, same constraint F-07 already established.

---

## Execution Plan

```
Phase 1 — Foundation (Parallel):
  T01 [P]  Extract SesErrorCodes/TemplateErrorCodes
  T02 [P]  RetryTopicChain
  T03 [P]  FailureClassification enum
  T04 [P]  Rename Api/Consumers → Api/Messaging
  T14 [P]  GSI3 + GetStuckDispatchingAsync (independent of the routing track)
  T15 [P]  ReconciliationOptions

Phase 2 — Core logic (Parallel, after Phase 1):
  T01, T03 ──→ T06 [P]  FailureClassifier
  T02, T03 ──→ T07 [P]  IFailureRouter interface
  T04 ──→ T08 [P]  IKafkaProducerFactory/KafkaProducerFactory
  T05 [P]  RetryContext (no dependencies)

Phase 3 — Router + Processor (Sequential):
  T02, T07, T08 ──→ T09  KafkaFailureRouter
  T05, T06, T07 ──→ T10  NotificationDispatchProcessor

Phase 4 — Consumers (Parallel, after T10):
  T09, T10 ──→ T11 [P]  Extend NotificationRequestedConsumer
  T02, T10 ──→ T12 [P]  RetryTopicConsumer
  T02 ──→ T13 [P]  DlqObserverHostedService
  T09, T14, T15 ──→ T16 [P]  ReconciliationHostedService

Phase 5 — Wiring (Sequential):
  T06, T08, T09, T10, T11, T12, T13, T15, T16 ──→ T17  DI wiring + Program.cs

Phase 6 — Observability & Docs (Parallel, after T17):
  T17 ──→ T18 [P]  OTEL metrics
  T17 ──→ T19 [P]  Update docs/architecture/overview.md
```

---

## Task Breakdown

### T01: Extract `SesErrorCodes`/`TemplateErrorCodes` constants [P]

**What**: Replace the inline `"Ses.SendFailed"`/`"Template.NotFound"`/`"Template.MissingField"`/`"Template.ParseError"` string literals with named constants
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Constants/SesErrorCodes.cs`, `TemplateErrorCodes.cs` (new); `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Email/SesEmailSender.cs`, `Templates/ScribanTemplateRenderer.cs` (modified to reference the constants instead of literals)
**Depends on**: None
**Reuses**: `ResilienceErrorCodes`'s exact shape (F-08)
**Requirement**: REL-01 (foundational — `FailureClassifier`, T06, needs these to exist)

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [x] `SesErrorCodes.SendFailed = "Ses.SendFailed"` defined and used in `SesEmailSender.cs`
- [x] `TemplateErrorCodes.NotFound = "Template.NotFound"`, `MissingField = "Template.MissingField"`, `ParseError = "Template.ParseError"` defined and used in `ScribanTemplateRenderer.cs`
- [x] No other behavior change — existing `SesEmailSenderTests`/`ScribanTemplateRendererTests` pass unmodified
- [x] `dotnet test --filter "Category!=Integration"` passes, 0 regressions

**Tests**: none
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all existing tests pass unchanged

**Commit**: `refactor(domain): extract SesErrorCodes and TemplateErrorCodes constants`

---

### T02: `RetryTopicChain` [P]

**What**: Static class holding the ordered topic-chain names and delay-lookup logic
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Constants/RetryTopicChain.cs`
**Depends on**: None
**Reuses**: n/a — new
**Requirement**: REL-04, REL-06, REL-09

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [x] `OriginalTopic`, `Retry5sTopic`, `Retry1mTopic`, `Retry10mTopic`, `DlqTopic` string constants defined, matching spec's exact topic names
- [x] `NextStage(int currentRetryCount): string` — `0→Retry5sTopic`, `1→Retry1mTopic`, `2→Retry10mTopic`, `>=3→DlqTopic`
- [x] `DelayFor(string topic): TimeSpan` — `Retry5sTopic→5s`, `Retry1mTopic→1m`, `Retry10mTopic→10m`; throws for any other topic (fail-fast on programmer error, not a runtime business outcome — per CLAUDE.md's narrower throw exception)
- [x] Unit tests: `NextStage` for each boundary (0,1,2,3,4); `DelayFor` for each valid topic and the throw case
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `RetryTopicChain` tests pass (5+ new tests)

**Commit**: `feat(domain): add RetryTopicChain`

---

### T03: `FailureClassification` enum [P]

**What**: `PoisonPill` | `Transient` enum
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Enums/FailureClassification.cs`
**Depends on**: None
**Reuses**: `NotificationStatus`/`Channel` enum precedent (E-02)
**Requirement**: REL-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `enum FailureClassification { PoisonPill, Transient }` defined
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add FailureClassification enum`

---

### T04: Rename `Api/Consumers` → `Api/Messaging` [P]

**What**: Move `IKafkaConsumerFactory.cs`, `KafkaConsumerFactory.cs`, `NotificationRequestedConsumer.cs` (and their namespaces) from `Api/Consumers/` to `Api/Messaging/`; move the corresponding test folder `Tests.Api/Consumers/` to `Tests.Api/Messaging/`
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Messaging/` (new location for the 3 files), `03-tests/06-Api/RentifyxCommunications.Tests.Api/Messaging/` (new location for `NotificationRequestedConsumerTests.cs`); all `using RentifyxCommunications.Api.Consumers;`/`namespace RentifyxCommunications.Api.Consumers;` references updated to `.Messaging`
**Depends on**: None
**Reuses**: n/a — mechanical move, confirmed with the user 2026-07-14 (the folder will otherwise hold both consumer and producer factories under a misleading name)
**Requirement**: n/a — housekeeping enabling T08/T11/T12/T13 to land in a correctly-named location

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] All 3 source files moved, namespace changed to `RentifyxCommunications.Api.Messaging`
- [ ] Test file moved, namespace changed to `RentifyxCommunications.Tests.Api.Messaging`, all `using`s updated
- [ ] `Program.cs`'s `AddHostedService<NotificationRequestedConsumer>()` registration unaffected in behavior (namespace import updated)
- [ ] `dotnet build --no-incremental` passes
- [ ] `dotnet test --filter "Category!=Integration"` passes, all 6 existing `NotificationRequestedConsumerTests` unchanged in behavior, 0 regressions

**Tests**: none
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → 0 regressions

**Commit**: `refactor(api): rename Consumers folder to Messaging`

---

### T05: `RetryContext` record [P]

**What**: Record carrying per-message retry state through the processor/router
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/RetryContext.cs`
**Depends on**: None
**Reuses**: `DispatchNotificationRequest`'s record style (E-03)
**Requirement**: REL-04, REL-06

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `RetryContext(string OriginalTopic, int RetryCount = 0, DateTimeOffset? FirstFailureTimestamp = null)` defined
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(application): add RetryContext`

---

### T06: `FailureClassifier` [P]

**What**: Maps `ErrorOr` error codes / caught exceptions to `FailureClassification` per spec's classification table
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/FailureClassifier.cs`
**Depends on**: T01, T03
**Reuses**: `SesErrorCodes`, `TemplateErrorCodes`, `ResilienceErrorCodes`, `NotificationErrorCodes` (Domain/Constants)
**Requirement**: REL-01, REL-02, REL-03

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Classify(IReadOnlyList<Error> errors): FailureClassification` — `Template.*` (all three), `Notification.Invalid*`, FluentValidation errors → `PoisonPill`; `Ses.SendFailed`, `Resilience.RateLimitExceeded`, `Resilience.CircuitOpen` → `Transient`; any unmatched code → `PoisonPill` (fail-closed, per spec REL-02)
- [ ] `Classify(Exception exception): FailureClassification` — `JsonException` → `PoisonPill`; any other exception → `Transient`
- [ ] Unit tests: one per row in the spec's classification table (11 rows), plus the fail-closed default case for an unmatched code
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `FailureClassifier` tests pass (12+ new tests)

**Commit**: `feat(application): add FailureClassifier`

---

### T07: `IFailureRouter` interface [P]

**What**: Domain contract for routing a failed message to the next retry stage or the DLQ
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/Notifications/IFailureRouter.cs`
**Depends on**: T02, T03
**Reuses**: `IEmailSender`'s placement/shape precedent (E-02)
**Requirement**: REL-04, REL-07, REL-10

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `RouteAsync(string rawMessage, RetryContext context, FailureClassification classification, string exceptionType, string exceptionMessage, CancellationToken ct): Task` defined
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(domain): add IFailureRouter interface`

---

### T08: `IKafkaProducerFactory` / `KafkaProducerFactory` [P]

**What**: Creates the `IProducer<Null, string>` used by `KafkaFailureRouter` and `ReconciliationHostedService`
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Messaging/IKafkaProducerFactory.cs`, `KafkaProducerFactory.cs`
**Depends on**: T04
**Reuses**: `KafkaConsumerFactory`'s exact config-reading pattern (E-03)
**Requirement**: REL-04

**Tools**:
- MCP: context7 (`ProducerBuilder<TKey,TValue>`/`ProducerConfig` API — already confirmed during Design, re-verify exact config property names at implementation time)
- Skill: none

**Done when**:
- [ ] `IKafkaProducerFactory.Create(): IProducer<Null, string>` defined
- [ ] `KafkaProducerFactory` reads the same `kafka` connection string as `KafkaConsumerFactory` via `IConfiguration.GetConnectionString("kafka")`
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(api): add KafkaProducerFactory`

---

### T09: `KafkaFailureRouter`

**What**: `IFailureRouter` implementation — decides the next topic via `RetryTopicChain` and publishes with the required headers
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Messaging/KafkaFailureRouter.cs`
**Depends on**: T02, T07, T08
**Reuses**: `RetryTopicChain.NextStage`
**Requirement**: REL-04, REL-05, REL-06, REL-07, REL-08, REL-09, REL-10

**Tools**:
- MCP: context7 (`IProducer.ProduceAsync` with `Headers` — already confirmed during Design)
- Skill: none

**Done when**:
- [ ] `PoisonPill` → publishes to `RetryTopicChain.DlqTopic`, `x-retry-count` unchanged from `context.RetryCount`
- [ ] `Transient` → publishes to `RetryTopicChain.NextStage(context.RetryCount)`, `x-retry-count = context.RetryCount + 1`, `x-next-retry-at = now + RetryTopicChain.DelayFor(nextTopic)` (only when the next stage isn't the DLQ — the DLQ has no further delay)
- [ ] All six required headers set on every publish: `x-original-topic`, `x-retry-count`, `x-first-failure-timestamp` (preserved from `context.FirstFailureTimestamp`, or set to `now` if this is the first failure), `x-exception-type`, `x-exception-message`, `x-next-retry-at` (DLQ publishes omit `x-next-retry-at` — nothing further will read it)
- [ ] Unit tests (mocked `IKafkaProducerFactory`/`IProducer`): PoisonPill → DLQ with correct headers; Transient from `RetryCount=0` → `retry-5s`; Transient from `RetryCount=2` (last retry stage) → `dlq`, not a 4th retry stage
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `KafkaFailureRouter` tests pass (4+ new tests)

**Commit**: `feat(infrastructure): add KafkaFailureRouter`

---

### T10: `NotificationDispatchProcessor`

**What**: Shared deserialize → invoke handler → classify → route logic, extracted from `NotificationRequestedConsumer.ProcessMessageAsync`
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Notifications/Handlers/Dispatch/NotificationDispatchProcessor.cs`
**Depends on**: T05, T06, T07
**Reuses**: `NotificationRequestedConsumer.ProcessMessageAsync`'s existing deserialization/handler-invocation code (E-03) — moved, not rewritten
**Requirement**: REL-01, REL-03, REL-04, REL-10

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `ProcessAsync(string rawMessage, RetryContext context, CancellationToken ct): Task` implemented
- [ ] `JsonException` during deserialization → `FailureClassifier`-independent direct `PoisonPill` routing (never reaches the handler) — matches E-03's existing malformed-message handling, now with routing added
- [ ] `outcome.IsError` → `FailureClassifier.Classify(outcome.Errors)`, then `IFailureRouter.RouteAsync` with the classification
- [ ] A successful `outcome` (including `Suppressed`) → no further action; the handler already persisted the terminal status (spec REL-03 — business-rule outcomes never reach classification)
- [ ] An unexpected exception from `handler.HandleAsync` itself (not an `ErrorOr` error) → `FailureClassifier.Classify(exception)`, then routed
- [ ] Unit tests: successful dispatch does not call the router; `outcome.IsError` with a poison-pill code routes to DLQ; `outcome.IsError` with a transient code routes to retry; malformed JSON routes to DLQ without invoking the handler; an exception thrown by the handler is classified and routed
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `NotificationDispatchProcessor` tests pass (5+ new tests)

**Commit**: `feat(application): add NotificationDispatchProcessor`

---

### T11: Extend `NotificationRequestedConsumer` to use the processor [P]

**What**: Replace `ProcessMessageAsync`'s inline logic with a call to `NotificationDispatchProcessor.ProcessAsync`, passing a `RetryContext` for the original topic (`RetryCount=0`)
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Messaging/NotificationRequestedConsumer.cs` (modified); `03-tests/06-Api/RentifyxCommunications.Tests.Api/Messaging/NotificationRequestedConsumerTests.cs` (updated — existing 6 tests must still pass, plus new ones for routing)
**Depends on**: T10
**Reuses**: The exact `StartAsync`/`StopAsync`/`ConsumeLoopAsync` lifecycle (E-03) — only `ProcessMessageAsync`'s body changes
**Requirement**: REL-04, REL-07

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `ProcessMessageAsync` now constructs `new RetryContext(NotificationRequestedConsumer.Topic)` and delegates to `NotificationDispatchProcessor.ProcessAsync` (resolved from the per-message DI scope, same pattern as the existing handler resolution)
- [ ] The consumer still always commits the offset after processing (unchanged from E-03 — spec's always-commit rule)
- [ ] Existing 6 tests (`StartAsync_LogsSubscription...`, `StopAsync_Cancels...`, `StartAsync_DoesNotThrow...`, `ConsumeLoop_WithValidMessage...`, `ConsumeLoop_WithMalformedJson...`, `ConsumeLoop_WhenHandlerThrows...`, `ConsumeLoop_WithMalformedMessageFollowedByValidMessage...`) pass with `IFailureRouter` mocked instead of asserting only on logs where they previously did
- [ ] New test: malformed JSON now results in `IFailureRouter.RouteAsync` called with `PoisonPill` (in addition to the existing "handler never called, still commits" assertions)
- [ ] New test: a transient `ErrorOr` failure from the handler results in `IFailureRouter.RouteAsync` called with `Transient`
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `NotificationRequestedConsumerTests` pass (7 existing + 2 new)

**Commit**: `feat(api): wire NotificationRequestedConsumer to NotificationDispatchProcessor`

---

### T12: `RetryTopicConsumer` [P]

**What**: Generic `IHostedService` for one retry stage — subscribes to its topic, waits out `x-next-retry-at` if not yet elapsed, delegates to `NotificationDispatchProcessor`
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Messaging/RetryTopicConsumer.cs`
**Depends on**: T02, T10
**Reuses**: `NotificationRequestedConsumer`'s `StartAsync`/`StopAsync`/`ConsumeLoopAsync` shape (E-03) — parametrized by topic instead of hardcoded
**Requirement**: REL-05, REL-06, REL-08

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Constructor accepts `topic: string` (which retry topic this instance subscribes to) alongside the existing dependencies (`IKafkaConsumerFactory`, `IServiceScopeFactory`, `IConfiguration`, logger)
- [ ] Before processing, reads `x-next-retry-at` from the message headers; if `now < x-next-retry-at`, `await Task.Delay(x-next-retry-at - now, token)` before proceeding
- [ ] Parses `x-retry-count`/`x-original-topic`/`x-first-failure-timestamp` headers into a `RetryContext`, then delegates to `NotificationDispatchProcessor.ProcessAsync`
- [ ] Always commits the offset after processing (same always-commit rule as the original consumer)
- [ ] Unit tests: a message with a future `x-next-retry-at` causes a measurable delay before the processor is invoked; a message with a past/elapsed `x-next-retry-at` is processed immediately; headers are correctly parsed into `RetryContext`
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `RetryTopicConsumer` tests pass (3+ new tests)

**Commit**: `feat(api): add RetryTopicConsumer`

---

### T13: `DlqObserverHostedService` [P]

**What**: Consumes `notification-requested-dlq`, logs prominently, and marks the notification `Failed` with `FailureReason` from `x-exception-message`
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Messaging/DlqObserverHostedService.cs`
**Depends on**: T02
**Reuses**: `NotificationRequestedConsumer`'s consumer-lifecycle shape (E-03), `INotificationRepository.UpdateStatusAsync` (F-07)
**Requirement**: REL-10, REL-11

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Subscribes to `RetryTopicChain.DlqTopic`
- [ ] On each message, logs at `Critical` level with the full header set (retry count, exception type/message, original topic) for triage visibility
- [ ] Deserializes the payload to extract the notification's `CorrelationId`/`Id` and calls `UpdateStatusAsync(id, NotificationStatus.Failed, ct)` — **Design note**: the DLQ message carries the same `DispatchNotificationRequest` payload; the notification's own `Id` isn't in that payload (only `CorrelationId` is) — Task must confirm whether `GetByIdAsync`-by-correlation or a lookup is needed, since `INotificationRepository` currently only supports `GetByIdAsync(Guid id)`, not by `correlationId` directly. If no such lookup exists, this step logs a warning and skips the status update rather than guessing — flagged explicitly in code, not silently wrong.
- [ ] Always commits the offset (DLQ is terminal — nothing to route further)
- [ ] Unit tests: a DLQ message triggers a `Critical` log with the header content; commit always happens
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `DlqObserverHostedService` tests pass (2+ new tests)

**Commit**: `feat(api): add DlqObserverHostedService`

---

### T14: GSI3 + `GetStuckDispatchingAsync` [P]

**What**: Add `GSI3PK`/`GSI3SK` to the `notifications` table schema, `NotificationItemMapper`, `DynamoDbNotificationRepository`, `INotificationRepository`, and the LocalStack test fixture
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Interfaces/Notifications/INotificationRepository.cs` (new method), `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/DynamoDbNotificationRepository.cs`, `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/NotificationItemMapper.cs`-equivalent (this repo's actual F-07 mapping is inline `ToItem`/`FromItem`, not a separate mapper class — confirm current shape before editing, don't assume the extracted-mapper structure from an earlier abandoned redo), `03-tests/05-Integration/RentifyxCommunications.Tests.Integration/Infrastructure/LocalStackNotificationInfrastructureFixture.cs` (add GSI3 to `CreateNotificationsTableAsync`)
**Depends on**: None
**Reuses**: F-07's existing `ToItem`/`FromItem` mapping pattern, `GSI1`/`GSI2` precedent in the same table
**Requirement**: REL-12, REL-13

**Tools**:
- MCP: context7 (`CreateTableAsync` with a third `GlobalSecondaryIndex` — same pattern as F-07's T09, extended)
- Skill: none

**Done when**:
- [ ] `ToItem` writes `GSI3PK = "STATUS#{status}"`, `GSI3SK = UpdatedAt` (or `CreatedAt` if `UpdatedAt` is null) on every write
- [ ] `INotificationRepository` gains `GetStuckDispatchingAsync(TimeSpan olderThan, CancellationToken ct): Task<IReadOnlyList<NotificationEntity>>`
- [ ] `DynamoDbNotificationRepository` implements it via a `Query` on `GSI3PK = "STATUS#Dispatching"` with a `GSI3SK <` filter for `now - olderThan`
- [ ] `LocalStackNotificationInfrastructureFixture.CreateNotificationsTableAsync` adds the `GSI3` index and its two attribute definitions
- [ ] Integration tests: seed a notification with `Status=Dispatching` and an old `UpdatedAt` → `GetStuckDispatchingAsync(2min)` returns it; seed one with a recent `UpdatedAt` → it's excluded; seed one with `Status=Sent` and an old `UpdatedAt` → excluded (status filter, not just staleness)
- [ ] `dotnet test` (full) passes

**Tests**: integration
**Gate**: full

**Verify**: `dotnet test --filter "Category=Integration&FullyQualifiedName~GetStuckDispatching"` → all pass (3+ new tests)

**Commit**: `feat(infrastructure): add GSI3 and GetStuckDispatchingAsync`

---

### T15: `ReconciliationOptions` [P]

**What**: Config-bound polling interval and staleness threshold
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Options/ReconciliationOptions.cs`
**Depends on**: None
**Reuses**: `ResilienceOptions`'s plain-record-for-config-binding style (F-08)
**Requirement**: REL-14

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `ReconciliationOptions(int PollIntervalSeconds = 60, int StalenessThresholdSeconds = 120)` defined
- [ ] `dotnet build --no-incremental` passes

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`

**Commit**: `feat(infrastructure): add ReconciliationOptions`

---

### T16: `ReconciliationHostedService` [P]

**What**: Polls every `PollIntervalSeconds` for notifications stuck in `Dispatching` longer than `StalenessThresholdSeconds`, republishes them to `notification-requested-retry-5s`
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Messaging/ReconciliationHostedService.cs`
**Depends on**: T09, T14, T15
**Reuses**: `NotificationRequestedConsumer`'s graceful `StopAsync` drain pattern (E-03), `PeriodicTimer` (BCL)
**Requirement**: REL-12, REL-13, REL-14

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `IHostedService` using a `PeriodicTimer(TimeSpan.FromSeconds(options.PollIntervalSeconds))` loop
- [ ] Each tick calls `GetStuckDispatchingAsync(TimeSpan.FromSeconds(options.StalenessThresholdSeconds))`, and for each result publishes to `RetryTopicChain.Retry5sTopic` via `IFailureRouter.RouteAsync` (classification = `Transient`, `RetryContext` with `RetryCount=0`, `OriginalTopic = RetryTopicChain.OriginalTopic`)
- [ ] A publish failure for one stuck record is logged and does not stop the loop from processing the rest, or from ticking again next interval (per spec Edge Cases — safe to re-run)
- [ ] `StopAsync` cancels the timer loop gracefully, mirroring `NotificationRequestedConsumer`'s drain pattern
- [ ] Unit tests: a stuck record triggers a `RouteAsync` call with the correct topic/context; a publish failure for one record doesn't prevent the loop from ticking again; no stuck records → no `RouteAsync` calls
- [ ] `dotnet test --filter "Category!=Integration"` passes

**Tests**: unit
**Gate**: quick

**Verify**: `dotnet test --filter "Category!=Integration"` → all `ReconciliationHostedService` tests pass (3+ new tests)

**Commit**: `feat(api): add ReconciliationHostedService`

---

### T17: Wire everything into `InfrastructureDependencyInjection` and `Program.cs`

**What**: Register `IKafkaProducerFactory`, `IFailureRouter`, `FailureClassifier`, `NotificationDispatchProcessor`, `ReconciliationOptions`, and all new `IHostedService`s (3× `RetryTopicConsumer`, `DlqObserverHostedService`, `ReconciliationHostedService`)
**Where**: `02-src/04-IoC/RentifyxCommunications.IoC/InfrastructureDependencyInjection.cs`, `02-src/01-Api/RentifyxCommunications.Api/Program.cs`
**Depends on**: T06, T08, T09, T10, T11, T12, T13, T15, T16
**Reuses**: The exact `AddHostedService<NotificationRequestedConsumer>()` registration pattern (E-03) for the new hosted services
**Requirement**: REL-01 through REL-16 (wiring)

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `IKafkaProducerFactory` registered `Singleton`; `IFailureRouter` → `KafkaFailureRouter` registered `Scoped` (matching other repository-adjacent registrations)
- [ ] `FailureClassifier`, `NotificationDispatchProcessor` registered `Scoped`
- [ ] `ReconciliationOptions` bound from a `Reconciliation` configuration section (same pattern as F-08's `ResilienceOptions`)
- [ ] `Program.cs` registers `AddHostedService<RetryTopicConsumer>()` three times with distinct topic parameters (or an equivalent factory-based registration, since `AddHostedService<T>` alone can't parametrize by constructor arg — confirm the correct DI pattern for 3 differently-configured instances of the same type during implementation), plus `AddHostedService<DlqObserverHostedService>()` and `AddHostedService<ReconciliationHostedService>()`
- [ ] `dotnet build --no-incremental` passes
- [ ] `dotnet test --filter "Category!=Integration"` passes, 0 regressions across the full unit suite

**Tests**: none
**Gate**: build

**Verify**: `dotnet build --no-incremental` → `0 Error(s)`; `dotnet test --filter "Category!=Integration"` → 0 regressions

**Commit**: `feat(ioc): wire F-09 retry/DLQ/reconciliation into DI`

---

### T18: OTEL metrics [P]

**What**: `kafka_consumer_lag_notification_requested` and `notification_dispatch_duration_seconds` exported via OpenTelemetry
**Where**: TBD during implementation — no existing custom-metric precedent in this codebase (Design flagged this needs fresh Context7 research against `OpenTelemetry.Extensions.Hosting`, already a dependency)
**Depends on**: T17
**Reuses**: n/a — new pattern for this codebase
**Requirement**: REL-15, REL-16

**Tools**:
- MCP: context7 (`OpenTelemetry.Extensions.Hosting` — `Meter`/`Counter`/`Histogram` instantiation and registration; not researched during Design, must be confirmed here per the Knowledge Verification Chain, not assumed)
- Skill: none

**Done when**:
- [ ] `notification_dispatch_duration_seconds` histogram records elapsed time from message receipt to outcome in `NotificationDispatchProcessor`
- [ ] `kafka_consumer_lag_notification_requested` reflects consumer group lag — exact source (Confluent.Kafka's built-in statistics callback vs. a manual high-watermark query) confirmed via Context7 before implementing, not assumed
- [ ] Metrics visible in the local Aspire Dashboard when running `dotnet run --project AppHost`
- [ ] `dotnet build --no-incremental` passes

**Tests**: unit if the metric-recording call can be asserted in isolation (e.g. via an injected `Meter`/test listener); otherwise manual verification via the Aspire Dashboard, documented as such
**Gate**: build

**Verify**: `dotnet run --project AppHost` → Aspire Dashboard shows both metrics after processing at least one message

**Commit**: `feat(observability): add OTEL metrics for consumer lag and dispatch duration`

---

### T19: Update `docs/architecture/overview.md` [P]

**What**: Document the retry/DLQ/reconciliation architecture, new topics, headers, and configuration keys
**Where**: `docs/architecture/overview.md`
**Depends on**: T17
**Reuses**: n/a — doc-only

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Diagram/prose reflects the retry topic chain, DLQ, and reconciliation job as implemented
- [ ] New `Reconciliation:PollIntervalSeconds`/`Reconciliation:StalenessThresholdSeconds` configuration keys documented
- [ ] `AWS Dev Account Requirements` and/or a new "Kafka Topics" section lists the 4 new topics (`-retry-5s`, `-retry-1m`, `-retry-10m`, `-dlq`)

**Tests**: none
**Gate**: n/a (documentation)

**Verify**: Manual review — content matches T02/T09/T17's actual implementation

**Commit**: `docs(architecture): update overview for F-09 reliability`

---

## Parallel Execution Map

```
Phase 1 (Parallel):
  T01 [P] ──┐
  T02 [P] ──┤
  T03 [P] ──┤
  T04 [P] ──┤  (all independent)
  T14 [P] ──┤
  T15 [P] ──┘

Phase 2 (Parallel, after Phase 1):
  T01, T03 ──→ T06 [P]
  T02, T03 ──→ T07 [P]
  T04 ──→ T08 [P]
  T05 [P] (no dependencies, could run in Phase 1 too)

Phase 3 (Sequential):
  T02, T07, T08 ──→ T09
  T05, T06, T07 ──→ T10

Phase 4 (Parallel, after T09/T10/T14/T15):
  T09, T10 ──→ T11 [P]
  T02, T10 ──→ T12 [P]
  T02 ──→ T13 [P]
  T09, T14, T15 ──→ T16 [P]

Phase 5 (Sequential):
  T06, T08, T09, T10, T11, T12, T13, T15, T16 ──→ T17

Phase 6 (Parallel, after T17):
  T17 ──→ T18 [P]
  T17 ──→ T19 [P]
```

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T01: Error code extraction | 4 files, mechanical | ✅ Granular |
| T02: RetryTopicChain | 1 class | ✅ Granular |
| T03: FailureClassification enum | 1 enum | ✅ Granular |
| T04: Folder rename | Mechanical move, 4 files | ✅ Granular (cohesive single operation) |
| T05: RetryContext | 1 record | ✅ Granular |
| T06: FailureClassifier | 1 class | ✅ Granular |
| T07: IFailureRouter | 1 interface | ✅ Granular |
| T08: KafkaProducerFactory | 2 files (interface + impl), cohesive | ✅ Granular |
| T09: KafkaFailureRouter | 1 class | ✅ Granular |
| T10: NotificationDispatchProcessor | 1 class | ✅ Granular |
| T11: Extend NotificationRequestedConsumer | 1 class (modified) + its test file | ✅ Granular |
| T12: RetryTopicConsumer | 1 class | ✅ Granular |
| T13: DlqObserverHostedService | 1 class | ✅ Granular |
| T14: GSI3 + repository method | Mapper + interface + repository + fixture, cohesive (one schema change) | ✅ Granular (2-3 related things, cohesive — F-07 precedent) |
| T15: ReconciliationOptions | 1 record | ✅ Granular |
| T16: ReconciliationHostedService | 1 class | ✅ Granular |
| T17: DI wiring | 2 files (extended) | ✅ Granular |
| T18: OTEL metrics | Scoped to 2 metrics, 1-2 files | ✅ Granular |
| T19: Docs update | 1 file | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T01 | None | No incoming arrow | ✅ Match |
| T02 | None | No incoming arrow | ✅ Match |
| T03 | None | No incoming arrow | ✅ Match |
| T04 | None | No incoming arrow | ✅ Match |
| T05 | None | No incoming arrow (shown as Phase 2, could be Phase 1) | ✅ Match |
| T06 | T01, T03 | T01 → T06, T03 → T06 | ✅ Match |
| T07 | T02, T03 | T02 → T07, T03 → T07 | ✅ Match |
| T08 | T04 | T04 → T08 | ✅ Match |
| T09 | T02, T07, T08 | T02 → T09, T07 → T09, T08 → T09 | ✅ Match |
| T10 | T05, T06, T07 | T05 → T10, T06 → T10, T07 → T10 | ✅ Match |
| T11 | T09, T10 | T09 → T11, T10 → T11 | ✅ Match |
| T12 | T02, T10 | T02 → T12, T10 → T12 | ✅ Match |
| T13 | T02 | T02 → T13 | ✅ Match |
| T14 | None | No incoming arrow | ✅ Match |
| T15 | None | No incoming arrow | ✅ Match |
| T16 | T09, T14, T15 | T09 → T16, T14 → T16, T15 → T16 | ✅ Match |
| T17 | T06, T08, T09, T10, T11, T12, T13, T15, T16 | All nine → T17 | ✅ Match |
| T18 | T17 | T17 → T18 | ✅ Match |
| T19 | T17 | T17 → T19 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| --- | --- | --- | --- | --- |
| T01 | Error code constants | none | none | ✅ OK |
| T02 | RetryTopicChain | unit | unit | ✅ OK |
| T03 | FailureClassification | none | none | ✅ OK |
| T04 | Folder rename | none | none | ✅ OK |
| T05 | RetryContext | none | none | ✅ OK |
| T06 | FailureClassifier | unit | unit | ✅ OK |
| T07 | IFailureRouter | none | none | ✅ OK |
| T08 | KafkaProducerFactory | none | none | ✅ OK |
| T09 | KafkaFailureRouter | unit | unit | ✅ OK |
| T10 | NotificationDispatchProcessor | unit | unit | ✅ OK |
| T11 | NotificationRequestedConsumer | unit | unit | ✅ OK |
| T12 | RetryTopicConsumer | unit | unit | ✅ OK |
| T13 | DlqObserverHostedService | unit | unit | ✅ OK |
| T14 | GSI3 + repository method | integration | integration | ✅ OK |
| T15 | ReconciliationOptions | none | none | ✅ OK |
| T16 | ReconciliationHostedService | unit | unit | ✅ OK |
| T17 | DI wiring | none | none | ✅ OK |
| T18 | OTEL metrics | unit/manual | unit/manual | ✅ OK |
| T19 | Docs | none | none | ✅ OK |

All three pre-approval checks pass — no restructuring needed.

---

## Requirement Coverage

All 16 `REL-*` requirements from `spec.md` map to at least one task above. No unmapped requirements.
