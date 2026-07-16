# State

**Last Updated:** 2026-07-15T18:00:00Z
**Current Work:** **E-01 through E-04 are all DONE and merged. E-05 (API Layer & LGPD Compliance) not yet started — next up.** E-01 via PR #2, E-02 via PR #4 (+ naming retrofit PR #5), E-03 via PR #6 (`DispatchNotificationHandler` full outbox sequence, `NotificationRequestedConsumer` wired to actually process messages, `Handle`→`HandleAsync` async-suffix retrofit). **E-04 F-07 (SES & DynamoDB Integration) is DONE and merged** via PR #7 (`feat/e04-f07-ses-dynamodb` → squash-merged to `main` as `8a28597`, 2026-07-13): `DynamoDbNotificationRepository`/`DynamoDbConsentRepository` (real single-table DynamoDB adapters — corrected key scheme, see AD-008 below), `ScribanTemplateRenderer` (resolves ADR-C05), `SesEmailSender`/`MockEmailSender` (env-gated via `IHostEnvironment` at DI resolution), all registered in `InfrastructureDependencyInjection`. 25 new tests (9 LocalStack Testcontainers integration + 16 unit). Enum-as-string persistence todo (below) is now resolved — `Channel`/`NotificationStatus` are stored via `.ToString()`, never numeric. **E-04 F-08 (Throttling & Circuit Breaking) merged** via PR #8 (`0488cd3`, 2026-07-14). **E-04 F-09 (Reliability — Retry, DLQ, Reconciliation) merged** via PR #9 (`8c16e4a`, 2026-07-15). **Scaffold cleanup merged** via PR #10 (`114c3f2`, 2026-07-15): removed `Examples` scaffold and EF Core/SQL Server package entirely (never matched the DynamoDB architecture — see resolved todo below), cleaned up `IOptions` config binding per CLAUDE.md's Configuration Binding rule. All merged-branch cleanups (local + remote) done same day; confirmed clean 2026-07-15 (all 10 PRs merged, no stale branches).

**⚠️ Branch divergence discovered and resolved (2026-07-14):** a separate Claude session on local `main` had independently re-implemented F-07 from scratch (12 local-only commits, never pushed) starting from the same commit PR #7 branched from — unaware PR #7 already existed and was merged to `origin/main`. Both implementations converged on the same functional shape but differed on the idempotency key scheme: the local redo kept `PK=NOTIF#{id}` and only discovered (via its own integration tests) that this makes the `ConditionExpression` a no-op, then patched around it with a `TransactWriteItemsAsync` two-item write. PR #7's `PK=NOTIF#{correlationId}` (AD-008) is simpler and needs no transaction. **Resolution:** local `main` was hard-reset to `origin/main` (PR #7's version kept as canonical); the local redo's 12 commits were pushed to a branch for reference, then that branch was deleted 2026-07-14 once its useful ideas were captured here and in the Todos below — not merged. See Deferred Ideas for a possible follow-up (PR #7's file layout doesn't match CLAUDE.md's mapper/folder-segmentation convention — the local redo's did). **Lesson:** before starting Execute on a feature, check for existing remote branches/PRs covering the same ROADMAP feature, not just local branch state.

**E-04 F-08 (Throttling & Circuit Breaking) is DONE and merged** (2026-07-14, `.specs/features/e04-f08-throttling/`, T01-T08 all complete, merged via PR #8 as `0488cd3`): a `ResilientEmailSender` decorator wraps F-07's `IEmailSender` (Ses/Mock) with a Polly `ResiliencePipeline<ErrorOr<Success>>` — generic, not exception-based, since `SendAsync` never throws (F-07 already maps SES exceptions to `ErrorOr`). Two corrections made during Design, confirmed real during Execute: (1) Polly v8 removed the v7 pure "N consecutive failures" circuit breaker (confirmed via Context7) — approximated via `FailureRatio = 1.0` + `MinimumThroughput = 5` + `SamplingDuration = 30s`; (2) the resilience pipeline is a `Singleton` (built once via `ResiliencePipelineFactory`, a static method — not folded into DI directly — specifically so it's unit-testable, since the IoC project has no test project of its own) injected into the `Scoped` `ResilientEmailSender`. `ResilienceOptions` genuinely binds from the `Resilience` configuration section (unlike `SecretsProviderOptions`, its closest precedent, which is actually never bound from config — just constructed with hardcoded defaults; confirmed by reading that code rather than assuming). `ResilienceStartupValidator` fails fast if `TokenBucketPermitsPerSecond`/`CircuitBreakerMinimumThroughput`/`CircuitBreakerSamplingDurationSeconds`/`CircuitBreakerBreakDurationSeconds` are `<= 0` — but `TokenBucketQueueMaxWaitSeconds = 0` is explicitly valid (means "reject immediately, never queue"), a distinction caught before it became a bug. T07's 1,000-notification burst load test (`Category=LoadTest`, on-demand via `dotnet test --filter "Category=LoadTest"`) confirmed real throttling by asserting elapsed time against a theoretical minimum, rather than brittle per-second bucket counting; `ci.yml`'s test filter was updated to also exclude `Category=LoadTest`. Scope explicitly excludes DLQ routing (F-09's concern — F-08 only marks `Failed`) and confirming the real SES quota (B-001, still open — ships with a configurable 14/s placeholder).

**E-04 F-09 (Reliability — Retry, DLQ & Reconciliation) is DONE and merged** (2026-07-14, `.specs/features/e04-f09-reliability/`, T01-T19 all complete, merged via PR #9 as `8c16e4a`, 2026-07-15): `NotificationDispatchProcessor` (Application, Scoped) is a shared deserialize → handle → classify → route pipeline extracted mid-Execute (not in the original design — added once the retry-topic consumers revealed the duplication) and reused identically by `NotificationRequestedConsumer` and all three `RetryTopicConsumer` instances, so every dispatch path (success, malformed JSON, classified failure) records `notification_dispatch_duration_seconds` uniformly via one `finally` block. `FailureClassifier` sorts failures into `PoisonPill` (malformed JSON, unknown template — DLQ immediately) or `Transient` (SES/rate-limit/circuit-breaker — retried); "template not found" was reclassified from business-rule to `PoisonPill` during Specify after confirming with the user that a missing template is a deployment defect, not a recoverable condition. `KafkaFailureRouter` (Infrastructure) publishes to the retry chain (`notification-requested-retry-5s` → `-1m` → `-10m` → dlq) with full traceability headers; `RetryTopicConsumer` waits out `x-next-retry-at` before reprocessing. `DlqObserverHostedService` logs `Critical` and marks the source notification `Failed`; `ReconciliationHostedService` polls a new `GSI3` (`STATUS#{status}`/`UpdatedAt`) on a `PeriodicTimer` for records stuck in `Dispatching`, republishing them as `Transient` with `RetryCount = 0`. Two Clean Architecture layering violations were caught by actually building after each new file (not assumed correct from the design doc) and fixed during Execute: `RetryContext` moved Application → `Domain/ValueObjects/` (Domain's `IFailureRouter` needed it), and `IKafkaProducerFactory`/`KafkaProducerFactory` moved `Api/Messaging/` → `Infrastructure/Messaging/` (Infrastructure's `KafkaFailureRouter` needed it, no Infrastructure→Api reference exists). Also fixed a pre-existing bug found along the way: `UpdateStatusAsync` never persisted `FailureReason` despite `NotificationEntity.MarkFailed(reason)` setting it in memory — added an optional `failureReason` parameter, wired both of `DispatchNotificationHandler`'s existing call sites. 0 regressions: 134 unit tests, 19/20 integration tests (the 1 failure is the known pre-existing `AppHostTests` AWS-profile gap, unrelated to this work — see Todos).

Next up overall: **E-04 is fully complete** — all local/remote feature branches (`feat/e04-f07-ses-dynamodb`, `feat/e04-f08-throttling`, `feat/e04-f09-reliability`) cleaned up 2026-07-15. **`.specs/features/tech-debt-scaffold-cleanup/` is DONE (2026-07-15), PR #10 open, not yet merged** — branch `chore/scaffold-cleanup-ioptions`, 9 commits (pushed and PR opened 2026-07-15): (1) removed the entire `Example` scaffold (EF Core/SQL Server included) across all 5 layers + 6 test projects, deleted now-unused generic repo interfaces (`IAddRepository<T>` etc.)/`PagedResult<T>`/`IUnitOfWork`; (2) cleaned up config reads — `Kafka`/`Resilience`/`Reconciliation`/`SecretsProvider` now use real `IOptions<T>` (they back DI-constructed singletons/hosted services); `Cors`/`RateLimit`/`OpenApi` use a plain typed record bound once inside their own startup extension method, no `IOptions<T>`/DI registration; `AWS` is untouched (`GetAWSOptions()` already binds it) — this rule is now codified in CLAUDE.md's "Configuration Binding" section (added 2026-07-15, after the user pushed back on an initial "IOptions everywhere" scope); (3) a user-requested hardcoded-values sweep after Execute found 3 more real issues, all fixed same day: the base `appsettings.json`'s `OpenTelemetry` JSON block (`OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production`, etc.) was entirely dead — `ServiceDefaults/Extensions.cs` reads `Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]` as a flat root key, so the nested `"OpenTelemetry": {...}` object was never actually read by anything; flattened the one live key to root and deleted the other 4 (they can only ever come from real OS env vars the OTEL SDK auto-detects, never from appsettings.json, regardless of nesting); `DynamoDbNotificationRepository.TableName` was a hardcoded `"notifications"` const with no config path — now `DynamoDbOptions.NotificationsTableName`, real `IOptions<T>` (DI-constructed Scoped repos), bound from a new `DynamoDb` appsettings section; GSI index/key names (`"GSI1"`/`"GSI1PK"`/etc.) were independently duplicated as raw string literals in 3 places (`DynamoDbNotificationRepository`, `NotificationItemMapper`, and the LocalStack test fixture that provisions the table) — extracted into one shared `Domain/Constants/NotificationTableSchema` used by all three, closing a silent-drift risk. Full build green, 126 unit tests passing. This also resolved two long-standing todos below (`CustomWebApplicationFactory` EF gap, "Examples scaffold doesn't match architecture") — both removed from Todos. The SES hardcoded `Subject = "RentifyX Notification"` (STATE.md Todos, below) was surfaced again during this sweep but intentionally left alone — it needs a real design decision (subject as template output? per-templateId config? Kafka payload field?), not a quick fix. **Follow-up (same day):** user caught that `KafkaOptions`/`DynamoDbOptions`/`SecretsProviderOptions` (all in `Application/Abstractions`) still had the real production values hardcoded as C# default parameters even after being wired to appsettings.json via `IOptions<T>` — two sources of truth for the same identity/environment values. Removed all three records' defaults, making them required from config only; fixed test call sites that relied on the parameterless constructor to pass explicit values instead (`LocalStackNotificationInfrastructureFixture` now exposes its own `TableName` constant shared by both DynamoDB repository test files). Lesson for future Options work: a record meant to be sourced from config should not also carry the real value as a hardcoded fallback — that's not "safety," it's a second place the value can drift from. **Further follow-ups (same day, before PR):** CA2213 fixed properly (delegate instead of holding a disposable field) rather than suppressed; CA2007 silenced in `.editorconfig` (not useful — no `SynchronizationContext` in ASP.NET Core); a repeated `TimeSpan.FromSeconds(30)`/`FromSeconds(1)` timeout pair duplicated across 4 Kafka hosted services extracted into `KafkaConsumerHostedServiceDefaults`; two logs (`GlobalExceptionHandler`, `DlqObserverHostedService`) were missing CorrelationId/TraceId that were already computed in scope — added; `docs/guides/adding-a-new-feature.md` rewritten (referenced deleted Example) and several pre-existing stale README claims fixed (E-04 status, missing GSI3, aspirational unimplemented endpoints/metrics/templates). **PR #10 opened 2026-07-15**: https://github.com/eugeniobandeira/rentifyx-communications-api/pull/10 — awaiting review/merge. After that: marketing campaigns (E-07) and identity-api integration contract (E-08), both spec'd but Execute not started.

**⚠️ `DispatchOutcome`→`DispatchNotificationResponse` rename silently lost, then restored (2026-07-14):** the earlier `git reset --hard origin/main` used to reconcile the F-07 branch divergence (see above) also reverted an unrelated later commit (`382373e`, `refactor(application): rename DispatchOutcome to DispatchNotificationResponse` + the matching CLAUDE.md convention update), because PR #7 had branched *before* that rename existed — resetting to PR #7's merged state reverted everything after the divergence point, not just the F-07 duplicate work. The user noticed the stale `DispatchOutcome` name reappearing mid-F-09-Execute and asked about it; the dropped commit was still reachable via its SHA (never garbage-collected), so it was restored faithfully via `git show 382373e` rather than redone from memory. **Lesson:** a `git reset --hard` to reconcile one divergence can silently revert *any* commit made after the divergence point, not just ones related to the specific conflict being fixed — worth a quick `git log --stat <reset-target>..<old-HEAD>` scan for anything unrelated before resetting, not just after.

**CLAUDE.md created 2026-07-13** documenting this repo's conventions (Entity suffix, interfaces segmented by domain folder, primary constructors for DI-only classes, constructors-only block-body rule, multi-line params, ErrorOr vs throw, no magic numbers/strings, enums persisted as string). One correction mid-session: an initial "block body for all methods" rule was over-broad — user clarified it's constructors only; regular/factory methods keep expression bodies freely.

**⚠️ Uncommitted, not-mine work found in the working tree (2026-07-13):** while verifying T17, discovered ~26 modified-but-uncommitted files unrelated to this session's tasks — mostly `using` reordering + BOM insertion (looks like an IDE code-cleanup pass), but at least one is a real functional change: `AppHost.cs` has `.WithKafkaUI()` added to the Kafka resource, which this session did not write. Left entirely untouched; every commit this session staged only its own intended files by explicit path. Flagged to the user directly — needs their review/decision, not a Claude action.

---

## Recent Decisions (Last 60 days)

### AD-014: Transactional Email defaults to opt-in (consent record represents opt-out) (2026-07-13)

**Decision:** When `IConsentRepository` has no `ConsentPreference` record for (recipient, Email channel), `Notification.Dispatch()` proceeds — absence of a record is NOT suppression. A record only blocks dispatch when it explicitly says `OptedIn = false`. Implemented as `ConsentDecision.NoRecordFound()` (`IsSuppressed = false`) vs. `ConsentDecision.FromPreference(pref)` (`IsSuppressed = !pref.OptedIn`).
**Reason:** Transactional email (verification, password reset, billing) has its own legal basis under LGPD Art. 7 and must not silently fail just because a producer service (e.g. identity-api) hasn't yet synced a consent record for a brand-new recipient. Blocking-by-default would risk breaking auth-critical flows (see E-08).
**Trade-off:** This is the opposite default from Marketing (E-07/AD-010), which is opt-out/default-deny. Two different defaults for two different channels is intentional, not an inconsistency — document this clearly wherever consent logic is touched so a future reader doesn't "fix" one to match the other.
**Impact:** `.specs/features/e02-domain-model/spec.md` NOTIF-04; `ConsentDecision` value object encodes the rule in exactly one place so callers (E-03's future handler) never have to re-derive it.

### AD-010: Marketing email added to v1 scope as E-07, sequenced last (2026-07-11)

**Decision:** Marketing campaign email (fan-out, unsubscribe, throughput isolation, bounce/complaint feedback) is in v1 scope, not deferred to a separate future feature — but implemented as E-07/E-08 after the transactional core (E-01–E-06) ships, so campaign work never risks the transactional SLOs already hardened in the original plan.
**Reason:** User wants this service usable for marketing from the start rather than re-scoping later, but campaign fan-out is structurally different (one-to-many, different consent channel, public unsubscribe, reputation risk) and must not be bolted onto the single-recipient dispatch path that E-04 hardened.
**Trade-off:** v1 timeline extends beyond the original 14–16 day estimate; exact new estimate pending Design/Tasks phase for E-07.
**Impact:** New feature specs `.specs/features/e07-marketing-campaigns/spec.md`; `Channel` enum gains a `Marketing` value with its own consent semantics (opt-out only, default-deny — see MKT edge cases); separate Kafka topic/consumer group/rate-limit budget from transactional.

### AD-011: identity-api integration is contract-only this cycle, migration deferred (2026-07-11)

**Decision:** Design and document the `NotificationRequested` contract against identity-api's auth-critical use cases (verification, password reset) now (E-08), but do not touch `rentifyx-identity-api` code in this cycle. Migration off its own direct-SES sender happens after communications-api's v1.0.0 has stabilized in production.
**Reason:** identity-api's own SES sending is already working in production; migrating it now would couple two repos' release timelines together before communications-api has proven itself. Locking the contract now avoids a breaking schema change later.
**Trade-off:** Duplicated SES-sending logic between the two services persists until the migration actually happens — must not be forgotten (tracked explicitly, not just implied).
**Impact:** New feature spec `.specs/features/e08-identity-integration/spec.md`; `docs/contracts/notification-requested.md` becomes the canonical schema both services reference; DLQ records for auth-critical templates get a `severity=auth-critical` tag so failures page instead of queueing passively.

### AD-013: LocalStack scope narrowed to automated tests only; manual dev run still uses real AWS (2026-07-12)

**Decision:** Refines AD-012. Manually running the app (`dotnet run --project AppHost`) still targets the real AWS dev/sandbox account via the named credentials profile (T07, already implemented). But automated integration tests (Testcontainers-based, run in CI and locally on-demand) use a LocalStack container instead of hitting the real dev account. Kafka in AppHost remains a local container either way (never affected).
**Reason:** User wants to occasionally spin up the app against real AWS data to validate true end-to-end integration, then tear the resources down — that's a deliberate, occasional, manual action, not something that should happen on every test run. Hitting real AWS on every `dotnet test` is slow, costs money, requires every CI run to have real AWS credentials, and risks tests polluting/depending on dev-account state.
**Trade-off:** Two code paths for AWS client configuration must both be kept correct — real profile-based credentials (manual dev) vs. LocalStack endpoint override (tests). Testcontainers LocalStack behavior can still diverge from real AWS at the margins (the original LocalStack objection from AD-012), but that risk is now confined to test-only scenarios, not dev/prod-shaped runs.
**Impact:** `.specs/features/e01-foundation/tasks.md` T12 reworked — `SecretsManagerProvider` integration tests now run against a LocalStack Secrets Manager container (Testcontainers), not the real dev-account Secrets Manager. Resolves the open CI-credential-strategy todo below (LocalStack needs no real AWS credentials in CI). E-07 F-14's original SNS/SQS-on-LocalStack test plan is correct as originally written — no rework needed after all.

### AD-012: Drop LocalStack — local dev targets a real AWS dev/sandbox account (2026-07-11)

**Decision:** Local development and integration testing do NOT use LocalStack. Instead, the AppHost and API connect to a real AWS dev/sandbox account (DynamoDB, SES, SecretsManager, KMS) via a named AWS credentials profile. Kafka remains a local container in Aspire (unaffected — Kafka has no LocalStack dependency).
**Reason:** User rejected LocalStack outright as providing no value for this workflow. Using the real AWS dev account also removes an entire class of emulation-parity bugs (LocalStack behavior diverging from real AWS) and removes the need for an init script to fabricate tables/identities that would otherwise need to exist for real in staging/prod anyway.
**Trade-off:** Every developer needs real AWS credentials (a named profile) configured locally, and dev-account AWS resources (DynamoDB tables, SES sender identity, Secrets Manager entries) must be provisioned before the app can run — this is currently a manual/deferred step, not automated by any init script. CI integration tests' AWS access strategy (same dev account vs. a dedicated CI IAM identity) is still an open decision (see Todos).
**Impact:** Supersedes the LocalStack container/init-script portions of E-01 F-01 (T07/T08 reworked — see `.specs/features/e01-foundation/tasks.md`). `.specs/features/e01-foundation/spec.md` US-C002 rewritten. E-07's SNS/SQS-on-LocalStack plan (F-14) also needs rework when E-07 execution starts — flagged, not yet rewritten task-by-task. README/PROJECT.md/ROADMAP.md updated to remove LocalStack from the tech stack and running-locally instructions.

---

### AD-001: ADR-C01 — Kafka-driven event intake, not synchronous HTTP (2026-07-03)

**Decision:** Notification requests are published as `NotificationRequested` Kafka events by producers; this service consumes them asynchronously.
**Reason:** Decouples producers from delivery timing and retry logic; matches the DynamoDB Streams event pattern already used in identity-api.
**Trade-off:** Producers lose synchronous confirmation of delivery — they only know the event was accepted, not that the email was sent.
**Impact:** No REST intake endpoint for notifications in v1; all dispatch flows through the Kafka consumer.

### AD-002: ADR-C02 — Channel-agnostic event schema from day one (2026-07-03)

**Decision:** `NotificationRequested` event includes a `channel` field and the `Channel` enum has `Sms`/`Push` reserved values even though only `Email` is implemented in v1.
**Reason:** Avoids a breaking schema change when SMS (likely leasing-api) or push is added later.
**Trade-off:** Slightly more ceremony in the domain model for unused enum values.
**Impact:** All consumer implementations must handle unknown channel values gracefully (route to DLQ, not crash).

### AD-003: ADR-C03 — Reuse SesEmailSender/MockEmailSender pattern from identity-api (2026-07-03)

**Decision:** Port and generalize the `SesEmailSender` from identity-api rather than building a new SES abstraction.
**Reason:** Proven pattern already in production; reduces net-new risk.
**Trade-off:** Creates a soft dependency on identity-api's implementation decisions — divergence must be tracked.
**Impact:** Infrastructure layer reuses the same `IEmailSender` abstraction shape.

### AD-004: ADR-C04 — Consent check inside communications-api, not trusted from producer (2026-07-03)

**Decision:** Before every send, this service checks `IConsentRepository` — it never trusts a producer claim that consent exists.
**Reason:** Centralizes LGPD Art. 8 compliance; prevents a misconfigured producer from sending to opted-out users.
**Trade-off:** Every dispatch incurs a DynamoDB read for consent.
**Impact:** `DispatchNotificationHandler` always checks consent before calling `IEmailSender`.

### AD-005: ADR-C05 — Server-side template rendering (Scriban or Razor Class Library) (2026-07-03)

**Decision:** Templates are rendered server-side; client/producer does not control rendering.
**Reason:** Keeps templates versioned and testable; avoids exposing template internals via API or event schema.
**Trade-off:** Template changes require a service deployment, not just a content update.
**Impact:** `ITemplateRenderer` / `ScribanTemplateRenderer` own the render pipeline; producers only supply payload data.

### AD-006: ADR-C06 — Kafka consumer as IHostedService inside the API host (2026-07-03)

**Decision:** `NotificationRequestedConsumer` runs as `IHostedService` in the same process as the Minimal API host.
**Reason:** Mirrors identity-api's `OutboxPublisher` pattern; single Helm chart, shared health checks and observability, no operational duplication.
**Trade-off:** Consumer and HTTP traffic scale together — if consumer load needs independent scaling, this decision must be revisited.
**Impact:** `Program.cs` registers the consumer as a hosted service; `StopAsync` must drain in-flight messages before shutdown.

### AD-007: ADR-C07 — Outbox-style persist-before-send lifecycle (2026-07-03)

**Decision:** Notification record is written with `Pending` status before SES is called; status flips to `Sent` only after SES confirms.
**Reason:** Prevents "sent but crashed before marking sent → retried → double-sent" failure.
**Trade-off:** Requires a reconciliation job (US-C022) to resolve records stuck in `Dispatching` after a crash.
**Impact:** Full status enum: Pending → Rendering → Dispatching → Sent | Failed | Suppressed; the `Dispatching` state is what enables crash recovery without duplicates.

### AD-008: ADR-C08 — Atomic idempotency via DynamoDB conditional write (2026-07-03, corrected 2026-07-13)

**Decision:** `INotificationRepository.SaveIfNotExistsAsync` uses DynamoDB `PutItem` with `ConditionExpression: attribute_not_exists(PK)`, where the notification item's `PK` is `NOTIF#{correlationId}` — **not** `NOTIF#{id}` as originally documented here.
**Reason:** Read-then-write has a race window during Kafka consumer rebalance — two consumers can both pass the read check before either writes. **2026-07-13 correction:** the original `PK=NOTIF#{id}` with a condition on the `correlationId` *attribute* was a no-op — `Id` is a fresh `Guid` generated on every `NotificationEntity.Create()` call, so no existing item could ever occupy that `PK` and the condition would always trivially pass, providing zero duplicate protection. `CorrelationId` is the value that actually repeats across Kafka re-deliveries, so it must be the partition key itself for `attribute_not_exists` to mean anything. Found and confirmed with the user while designing E-04 F-07.
**Trade-off:** DynamoDB conditional writes have cost implications under very high throughput — acceptable for notification volumes. `Id` is no longer the partition key, so `GetByIdAsync` needs a GSI (`GSI2`, keyed `ID#{id}`) instead of a direct `GetItem`.
**Impact:** A duplicate `correlationId` write fails the condition; the handler treats this as success (ack the message, no reprocessing). See `.specs/features/e04-f07-ses-dynamodb/design.md` for the full corrected table schema (`GSI1`=`RECIPIENT#{recipientId}`, `GSI2`=`ID#{id}`).

### AD-009: ADR-C09 — Local token-bucket rate limiter + Polly circuit breaker in front of IEmailSender (2026-07-03)

**Decision:** A token-bucket limiter (sized below account SES sending-rate quota) and a Polly circuit breaker wrap `IEmailSender`.
**Reason:** Prevents self-inflicted SES `Throttling` cascades during bursts (e.g., bulk asset approvals); circuit breaker fails fast when SES itself is degraded.
**Trade-off:** Token bucket quota must be sized against the actual confirmed SES quota shared with identity-api — do not guess.
**Impact:** `US-C019` implements the limiter; `US-C020` implements the circuit breaker; `US-C032` load test validates both hold under 1,000-event burst.

---

## Active Blockers

### B-001: SES account sending-rate quota unconfirmed (2026-07-03)

**Discovered:** 2026-07-03
**Impact:** Token-bucket limiter (US-C019) cannot be correctly sized until the actual SES quota is known; shared with identity-api, so joint load matters.
**Workaround:** Use a conservative placeholder (e.g., 14 emails/sec) during development; do not finalize before confirming actual quota.
**Resolution:** Confirm the account's SES sending quota via AWS console or `aws ses get-send-quota` before implementing US-C019.

---

## Lessons Learned

*(none yet — project just initialized)*

---

## Quick Tasks Completed

| #   | Description | Date | Commit | Status |
| --- | ----------- | ---- | ------ | ------ |
| —   | —           | —    | —      | —      |

---

## Deferred Ideas

- [ ] Event contract versioning ADR — needs to be written before leasing-api integration begins. Captured during: project initialization.
- [ ] Token-bucket resizing strategy across transactional + campaign + identity-api load once all three are live. Captured during: project initialization; updated 2026-07-11 to include campaign traffic.
- [ ] identity-api code migration off its own `SesEmailSender` (publish `NotificationRequested` instead) — trigger: communications-api v1.0.0 stable in production for an agreed window. Owner: write the migration ADR in `rentifyx-identity-api`'s own `.specs/`, not here — this repo only owns the contract side (E-08). Captured during: 2026-07-11 scope discussion.
- [ ] Campaign creation/management UI or admin API — only if manually publishing `CampaignRequested` events proves insufficient in practice. Captured during: E-07 scoping, 2026-07-11.
- [x] SES bounce/complaint webhook processing — was v1.1 backlog, promoted into v1 scope as E-07 F-14 (MKT-04) since marketing volume makes reputation risk real. Captured during: project initialization; resolved into roadmap 2026-07-11.

---

## Todos

- [ ] **E-06 F-11 (Terraform/Helm) now depends on shared Kafka infra, not self-provisioned.** 2026-07-15 cross-repo planning session confirmed neither this repo nor `rentifyx-identity-api` has ever provisioned a real Kafka broker for production (both only run a local Aspire dev container). Decision: Kafka is cross-service infra, so it's specced in `rentifyx-platform` (`.specs/features/shared-kafka-eks/spec.md`) — self-hosted on the shared EKS cluster via Helm (not AWS MSK, cost-driven), broker address published via SSM Parameter Store per that repo's ADR-005 convention. F-11's Terraform must consume that SSM value, not provision its own broker. `rentifyx-identity-api` is also building a producer side (`.specs/features/outbox-kafka-notifications/spec.md`) to replace its current direct-SES-send with `NotificationRequested` publish — that repo's R-07 depends on this repo's exact Kafka message contract (see AD-002) staying stable, and ideally on E-08 F-15's `docs/contracts/notification-requested.md` existing before its design phase.
- [ ] Confirm actual AWS SES sending-rate quota (account-level) before implementing US-C019 token bucket — resolves B-001.
- [ ] Decide on Scriban vs. Razor Class Library for template rendering (ADR-C05) — currently open choice, both are valid.
- [x] Verify identity-api's Kafka broker config is reusable for local dev, or set up standalone Kafka container in AppHost (US-C002) — resolved by T09: standalone `AddKafka("kafka")` in AppHost, KRaft mode.
- [ ] `AppHostTests` (T04's health-check test + T09's Kafka test) boots the real API process, not LocalStack — it hits T07's fail-fast `AWS:Profile` check same as a manual run. Works locally once `dotnet user-secrets set "AWS:Profile" "<profile>"` is set on the Api project. T13 tagged it `Category=Integration` and excluded it from the CI test filter, so it no longer breaks CI — but it also isn't exercised in CI at all now. Still needs a real fix (e.g. a dedicated CI-only AWS identity, or point `AppHostTests` at LocalStack too) so CI actually verifies the AppHost boots. Discovered 2026-07-12 while verifying T09.
- [ ] T13's coverage gate is live at the spec'd 80% threshold, but actual repo coverage is ~5.6% (E-01 is foundational scaffolding; `Examples` feature is template boilerplate with skipped placeholder tests) — CI (`build-test-coverage` job) will be red on every push/PR until real unit tests land alongside E-02+ feature work. This is intentional per explicit user decision on 2026-07-12, not a bug to silently fix.
- [ ] `AppHostTests`' health-check test now *also* fails for a second reason (on top of the AWS:Profile gap above): T12's `SecretsStartupValidator` requires `rentifyx/comms/ses-arn`, `rentifyx/comms/kafka-sasl-username`, `rentifyx/comms/kafka-sasl-password` to actually exist in the real AWS dev/sandbox account's Secrets Manager, and per T08 that provisioning was explicitly scoped as a manual step never done. Left as a known gap per explicit user decision on 2026-07-13 — same test, same "not exercised end-to-end anywhere" problem as the row above; whoever eventually fixes the AppHostTests/CI gap should provision these 3 secrets (or point AppHostTests at LocalStack too) in the same pass. `AppHost_StartsKafkaResource_AndBrokerIsReachable` is unaffected.
- [ ] `.github/workflows/ci.yml`'s `trivy-scan` job pins `aquasecurity/trivy-action` to a commit SHA (`ed142fd0...` / `v0.36.0`, chosen 2026-07-13 after the March 2026 tag-hijack supply-chain attack on that action). SHA-pinning means it will never auto-update — periodically check for newer signed releases and re-pin manually; don't switch back to a floating tag.
- [ ] T15's `owasp-check` CI job needs an `NVD_API_KEY` repository secret to actually run (README documents how to get one and where to add it) — not set up yet, this is a manual step in GitHub repo Settings → Secrets and variables → Actions, outside what a coding session can do. The job will fail (or run very slowly/get 403'd) until it's added. `owasp/dependency-check` is pinned to an image digest (12.2.2, 2026-05-03) rather than `:latest` — check for newer releases periodically and re-pin the digest.
- [x] `CustomWebApplicationFactory` (`03-tests/05-Integration`) was broken in three compounding, dormant ways (EF InMemory provider never wired, "Testing" vs "Test" environment name mismatch, no InMemory package referenced) — moot as of 2026-07-15's `tech-debt-scaffold-cleanup`: its only consumer (`ExampleEndpointTests`) and EF Core itself are both gone, so the factory was deleted rather than fixed.
- [x] E-04 (DynamoDB implementation): `Channel` and `NotificationStatus` enums must be persisted as their **string name** (e.g. `"Sent"`), never as the underlying `int` — user explicitly rejected numeric enum storage on 2026-07-13 ("fica sem sentido"). Resolved: `DynamoDbNotificationRepository`/`DynamoDbConsentRepository` use `.ToString()`/`Enum.Parse<T>()` throughout, verified in F-07's LocalStack integration tests.
- [x] The `Examples` scaffold's persistence layer (`AppDbContext`, `ExampleRepository`, EF Core + SQL Server) didn't match the project's actual DynamoDB architecture (AD-008) — resolved 2026-07-15 via `tech-debt-scaffold-cleanup`: the entire Example feature and EF Core/SQL Server package were removed, not extended.
- [ ] `SesEmailSender` (F-07) sends every email with a hardcoded `Subject = "RentifyX Notification"` — `IEmailSender.SendAsync(EmailAddress, string renderedContent, ...)` has no subject parameter, and no template metadata carries one either. Fine for F-07's scope (exercising the pipeline end-to-end), but real notification types (e.g. `AssetApprovedEmail`) will need distinct subject lines before this ships to production. Needs a design decision (subject as a second template output? a per-`templateId` config? part of the Kafka payload?) before E-05/E-08 content work lands.
- [x] PR #7's F-07 file layout didn't match CLAUDE.md's folder-segmentation convention: `DynamoDbNotificationRepository`/`DynamoDbConsentRepository` lived directly under `Repositories/` with mapping done via private `ToItem`/`FromItem` methods inline in each repository class. Discovered 2026-07-14 during the branch-divergence reconciliation; resolved same day — both repositories moved to `Repositories/Notifications/`, mapping extracted into dedicated static `NotificationItemMapper`/`ConsentItemMapper` classes. No behavior change; all integration/unit tests re-verified passing.

---

## Preferences

**Model Guidance Shown:** never
