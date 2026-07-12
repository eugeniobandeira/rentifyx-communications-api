# E-07 · Marketing Email Campaigns — Tasks

**Design**: `.specs/features/e07-marketing-campaigns/design.md`
**Status**: Draft

**2026-07-11 note (AD-012)**: LocalStack was dropped from this project — this task list's integration tests target the real AWS dev/sandbox account (DynamoDB, SQS/SNS) instead. Not yet rewritten task-by-task since E-07 Execute hasn't started; T13 below still needs a full rework pass when execution begins.

---

## Test Strategy (TESTING.md not yet created — defined inline, mirrors E-01's convention)

| Gate | Command | When |
|---|---|---|
| **build** | `dotnet build --no-incremental` | Config/wiring/IaC tasks with no testable business logic |
| **quick** | `dotnet test --filter "Category!=Integration"` | Unit tests only |
| **full** | `dotnet test` | Unit + integration (real AWS dev/sandbox account) |

**Parallelism:**
- Unit tests → parallel-safe ✅
- Integration tests (real AWS dev/sandbox account: DynamoDB, SQS/SNS, WebApplicationFactory) → NOT parallel-safe ❌ (shared table/resource lifecycle)
- Build-only / IaC validate → parallel-safe ✅

---

## Execution Plan

```
Phase 1 — Domain Foundation (Sequential):
  T01 ──→ T02 ──→ T03

Phase 2 — Parallel Tracks (after T03):
                   ┌──→ T04 ──→ T05
  T03 complete ────┼──→ T06 [P] ──→ T07
                   ├──→ T08 [P]
                   └──→ T12 [P]

Phase 3 — Infrastructure (depends on Phase 2):
  T01 + T08 ──→ T09
  T04        ──→ T10
  T05        ──→ T11
  T12        ──→ T13

Phase 4 — API & Wiring:
  T06 + T07             ──→ T14
  T08                    ──→ T15
  T05 + T12 + T09 + T08  ──→ T16

Phase 5 — Validation, IaC & Docs (Sequential tail):
  T16 + T10 + T11 ──→ T17
  T13             ──→ T18
  T17 + T18       ──→ T19
```

---

## Task Breakdown

### T01: Extend `Notification` aggregate for campaign fields

**What**: Add `CampaignId` (nullable) and `Category` (`Transactional` | `Campaign`) to the existing `Notification` aggregate; add `Marketing` to the `Channel` enum (was `Email` only in E-02, reserved slots existed for `Sms`/`Push` — `Marketing` is new and orthogonal, it's a consent category not a delivery channel, so confirm it doesn't collide with the `Channel` enum and instead lives as a value on `ConsentPreference`/dispatch context per design)
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Entities/Notification.cs`, `.../Enums/` (Category enum)
**Depends on**: None (assumes E-02's `Notification` aggregate exists — if E-01–E-06 haven't shipped yet, this task also creates the minimal aggregate shape needed)
**Reuses**: E-02's `Notification` aggregate and `AggregateRoot` base
**Requirement**: MKT-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `Notification.CampaignId` (nullable `string`) and `Notification.Category` (enum, default `Transactional`) exist
- [ ] Existing transactional construction paths default `Category = Transactional`, `CampaignId = null` — zero behavior change for E-01–E-06 flows
- [ ] `dotnet build` passes with zero warnings

**Tests**: none (structural change, covered by T03)
**Gate**: build — `dotnet build --no-incremental`

**Commit**: `feat(domain): add campaign fields to Notification aggregate`

---

### T02: Define `CampaignSummary` model + `ICampaignRepository` + `CampaignRequested` contract

**What**: Domain model for `CampaignSummary` (totalRecipients, sentCount, suppressedCount, failedCount, pendingCount, createdAt, completedAt), `ICampaignRepository` interface (`CreateIfNotExists`, `IncrementCounter`, `GetSummary`), and the `CampaignRequested` Kafka event DTO (campaignId, templateId, recipientIds[], payload)
**Where**: `02-src/03-Domain/RentifyxCommunications.Domain/Entities/CampaignSummary.cs`, `.../Interfaces/ICampaignRepository.cs`, `02-src/02-Application/RentifyxCommunications.Application/Features/Campaigns/CampaignRequested.cs`
**Depends on**: T01
**Reuses**: `INotificationRepository` interface shape as a pattern reference (E-02)
**Requirement**: MKT-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `ICampaignRepository` has exactly the three methods from design.md, no more
- [ ] `CampaignSummary` has no framework/AWS references (pure domain, per existing layer rule)
- [ ] `dotnet build` passes with zero warnings

**Tests**: none (contracts only, covered by T03/T04)
**Gate**: build — `dotnet build --no-incremental`

**Commit**: `feat(domain): add CampaignSummary model, ICampaignRepository, CampaignRequested contract`

---

### T03: Domain unit tests — campaign fields & default-deny consent

**What**: Unit tests proving `Notification` correctly carries `CampaignId`/`Category`, and that a recipient with no consent record is treated as opted-out (default-deny, spec edge case) for the `Marketing` channel
**Where**: `03-tests/03-Handlers/` or domain test project (mirror E-02's `Notification` test location)
**Depends on**: T01, T02
**Reuses**: Existing Bogus builders pattern from `01-Common`
**Requirement**: MKT-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Test: `Notification` created with `Category = Campaign` and a `CampaignId` round-trips correctly
- [ ] Test: `ConsentPreference` lookup miss for `Marketing` channel resolves to opted-out, not opted-in
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 2+ new tests pass (no silent deletions elsewhere)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `test(domain): cover campaign fields and default-deny consent rule`

---

### T04: Implement `CampaignFanOutHandler`

**What**: Application handler that validates the recipient-list ceiling (reject >50,000), creates/updates the `CampaignSummary`, and for each recipient: checks consent, performs the per-recipient idempotent save (`CAMPAIGN#{campaignId}#{recipientId}`), and invokes the existing `DispatchNotificationHandler` — catching and logging individual failures without failing the batch
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Campaigns/FanOut/CampaignFanOutHandler.cs`
**Depends on**: T02
**Reuses**: `DispatchNotificationHandler` (E-03) unmodified, `IConsentRepository` (E-04), handler-pattern conventions from E-03
**Requirement**: MKT-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Empty `recipientIds[]` → ack, zero notifications created, summary reports all-zero counts
- [ ] `recipientIds[]` > 50,000 → rejected at the boundary, no partial processing, clear log entry
- [ ] Opted-out recipient → `Suppressed` status, SES never invoked for them
- [ ] One recipient's render/send failure does not stop processing of the rest of the batch
- [ ] Duplicate `campaignId + recipientId` (simulated replay) → exactly one dispatch occurs
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 5+ new tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `feat(application): implement CampaignFanOutHandler`

---

### T05: Implement `CampaignRequestedConsumer` [P]

**What**: Kafka `IHostedService` consuming the `campaign-requested` topic, mapping messages to `CampaignRequested` and invoking `CampaignFanOutHandler`; malformed payloads are logged and routed to DLQ, not silently dropped
**Where**: `02-src/01-Api/RentifyxCommunications.Api/CampaignRequestedConsumer.cs`
**Depends on**: T04
**Reuses**: `NotificationRequestedConsumer`'s `IHostedService` registration/drain pattern (ADR-C06, E-03)
**Requirement**: MKT-01

**Tools**:
- MCP: `context7` (Confluent.Kafka consumer API reference, if unfamiliar patterns arise)
- Skill: none

**Done when**:
- [ ] Consumer maps a valid Kafka message to `CampaignRequested` and calls the handler
- [ ] Malformed payload → logged, routed to DLQ path, does not crash the consumer loop
- [ ] `StopAsync` drains the in-flight batch before shutdown, mirroring the transactional consumer
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 2+ new tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `feat(api): add CampaignRequestedConsumer hosted service`

---

### T06: Implement `UnsubscribeTokenService` [P]

**What**: Issue and verify compact HMAC-SHA256-signed unsubscribe tokens (`base64url(payload).base64url(HMAC)`), 90-day expiry, no external JWT library
**Where**: `02-src/02-Application/RentifyxCommunications.Application/Features/Unsubscribe/UnsubscribeTokenService.cs`
**Depends on**: T03 (Phase 2 start gate — no direct code dependency, sequenced after domain foundation for clean parallel-track kickoff)
**Reuses**: `ISecretsProvider` startup-loading convention (E-01)
**Requirement**: MKT-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `IssueToken` produces a token that `TryVerify` accepts for the same recipientId/channel
- [ ] Expired token → `TryVerify` returns an error, not a silent false-accept
- [ ] Tampered payload (signature mismatch) → rejected
- [ ] Replaying a valid, non-expired token twice → both verifications succeed (stateless, no nonce store — this IS the idempotency mechanism, not a bug)
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 4+ new tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `feat(application): implement UnsubscribeTokenService`

---

### T07: Extend template renderer with `unsubscribeUrl` + load signing key secret

**What**: Add `unsubscribeUrl` as an injected template variable for `Marketing`-category renders (built from `UnsubscribeTokenService.IssueToken`), and register the new `rentifyx/comms/unsubscribe-signing-key` secret in the existing `ISecretsProvider` startup load (fail-fast if missing, matching existing convention)
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/` (`ScribanTemplateRenderer.cs`, secrets loader)
**Depends on**: T06
**Reuses**: `ScribanTemplateRenderer` (E-03), `ISecretsProvider` (E-01)
**Requirement**: MKT-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] A `Marketing`-category render includes a working `unsubscribeUrl` in the output
- [ ] A `Transactional`-category render is unaffected (no unsubscribe link injected)
- [ ] Missing `unsubscribe-signing-key` secret at startup → fail-fast critical log + process exit, same as existing SES/Kafka secrets
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 2+ new tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `feat(infrastructure): inject unsubscribe link into marketing templates`

---

### T08: Implement `DynamoDbCampaignRepository` [P]

**What**: `CreateIfNotExists` (idempotent on replayed campaign events), `IncrementCounter` (atomic `UpdateItem ... ADD`, no read-then-write), `GetSummary` against the existing single DynamoDB table using the `CAMPAIGN#{campaignId}` key
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/DynamoDbCampaignRepository.cs`
**Depends on**: T02 (Phase 2 start gate)
**Reuses**: Existing DynamoDB single-table connection/client setup (E-04)
**Requirement**: MKT-01

**Tools**:
- MCP: `context7` (AWSSDK.DynamoDBv2 `UpdateItem` conditional/atomic-ADD API reference)
- Skill: none

**Done when**:
- [ ] `CreateIfNotExists` called twice with the same `campaignId` results in exactly one summary record
- [ ] `IncrementCounter` called concurrently (simulated race) results in correct final counts — no lost updates
- [ ] `GetSummary` returns correct aggregate counts after a mixed batch of sent/suppressed/failed
- [ ] Gate check passes: `dotnet test` (real AWS dev/sandbox account DynamoDB)
- [ ] Test count: 4+ new tests pass (no silent deletions)

**Tests**: integration
**Gate**: full — `dotnet test`

**Commit**: `feat(infrastructure): implement DynamoDbCampaignRepository with atomic counters`

---

### T09: Extend `DynamoDbNotificationRepository` for campaign idempotency + GSI3

**What**: Extend the existing conditional-write idempotency (ADR-C08) to accept the composite key `CAMPAIGN#{campaignId}#{recipientId}` for campaign notifications, and add `GSI3 = CAMPAIGN#{campaignId}` for per-recipient outcome lookups within a campaign
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Repositories/DynamoDbNotificationRepository.cs`, table schema/init script
**Depends on**: T01, T08
**Reuses**: Existing `SaveIfNotExists` conditional-write implementation and GSI pattern (E-04, ADR-C08)
**Requirement**: MKT-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Two concurrent handlers racing on the same `campaignId + recipientId` → exactly one succeeds in creating the record (same race test shape as US-C013's transactional test, applied to campaign keys)
- [ ] `GSI3` query by `campaignId` returns all per-recipient notification records for that campaign
- [ ] Existing transactional `correlationId` idempotency path is unaffected (regression check)
- [ ] Gate check passes: `dotnet test` (real AWS dev/sandbox account DynamoDB)
- [ ] Test count: 3+ new tests pass, 0 existing tests broken

**Tests**: integration
**Gate**: full — `dotnet test`

**Commit**: `feat(infrastructure): extend notification repository for campaign idempotency and GSI3`

---

### T10: Campaign-isolated token bucket + circuit breaker

**What**: A second instance of the token-bucket + Polly circuit breaker (ADR-C09 pattern) wrapping `IEmailSender`, sized independently from the transactional bucket, used only by the campaign dispatch path
**Where**: `02-src/05-Infrastructure/RentifyxCommunications.Infrastructure/Resilience/CampaignRateLimiter.cs`
**Depends on**: T04
**Reuses**: Existing token-bucket/circuit-breaker implementation from E-04 (ADR-C09) — same class, second configured instance, not new logic
**Requirement**: MKT-03

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Campaign bucket exhaustion does not affect the transactional bucket's token availability (isolated state, verified by test)
- [ ] Circuit breaker on the campaign path opens independently from the transactional path's circuit breaker
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 2+ new tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `feat(infrastructure): isolate campaign rate limiter and circuit breaker from transactional`

---

### T11: Isolated Kafka topic/consumer group configuration

**What**: Configure `campaign-requested` as a distinct topic with its own consumer group ID, confirmed isolated from `notification-requested`'s consumer group in both AppHost (local) and production config
**Where**: `01-aspire/01-AppHost/RentifyxCommunications.AppHost/` config, `appsettings.json` (Kafka section)
**Depends on**: T05
**Reuses**: Existing Kafka broker/topic configuration pattern from E-01/E-03
**Requirement**: MKT-03

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] `campaign-requested` topic is created by the Kafka init (dev-account resources are provisioned manually, not via a LocalStack init script — AD-012) alongside `notification-requested`
- [ ] Consumer group IDs are distinct and documented (e.g., `comms-campaign-consumer` vs `comms-notification-consumer`)
- [ ] `dotnet run --project AppHost` boots both consumers cleanly, visible in Aspire dashboard
- [ ] `dotnet build` passes

**Tests**: none (infrastructure config)
**Gate**: build — `dotnet build --no-incremental`

**Commit**: `chore(infra): isolate campaign Kafka topic and consumer group`

---

### T12: Implement `SesFeedbackConsumer` [P]

**What**: `IHostedService` that long-polls the SQS queue receiving SNS-wrapped SES bounce/complaint notifications, filters for hard bounce (`bounceType == Permanent`) or complaint (ignoring soft/transient bounces), and applies auto opt-out via the existing `IConsentRepository`
**Where**: `02-src/01-Api/RentifyxCommunications.Api/SesFeedbackConsumer.cs`
**Depends on**: T02 (Phase 2 start gate — no direct code dependency on fan-out track)
**Reuses**: `IConsentRepository` (E-04), `IHostedService` lifecycle pattern (ADR-C06)
**Requirement**: MKT-04

**Tools**:
- MCP: `context7` (AWSSDK.SQS / SNS message envelope format reference)
- Skill: none

**Done when**:
- [ ] Hard bounce notification → `ConsentPreference(Marketing, OptedIn=false)` for that recipient
- [ ] Soft (transient) bounce → no consent change
- [ ] Complaint notification → auto opt-out, same as hard bounce
- [ ] Suppression is per-channel: a `Marketing` opt-out from bounce feedback does not touch `Transactional` consent
- [ ] Gate check passes: `dotnet test --filter "Category!=Integration"`
- [ ] Test count: 4+ new tests pass (no silent deletions)

**Tests**: unit
**Gate**: quick — `dotnet test --filter "Category!=Integration"`

**Commit**: `feat(api): add SesFeedbackConsumer for bounce/complaint auto opt-out`

---

### T13: Dev-account SNS + SQS for SES feedback (REWORKED per AD-012, 2026-07-11 — needs full pass when E-07 execution starts)

**What**: Provision real SNS topic and SQS queue resources in the AWS dev/sandbox account, with the SQS queue subscribed to the SNS topic, so `SesFeedbackConsumer` (T12) has something to poll. No LocalStack container/init script — mirrors E-01 T07/T08's AD-012 rework (AWS SDK config against the real account; resource existence documented, not auto-provisioned by this service).
**Where**: `01-aspire/01-AppHost/RentifyxCommunications.AppHost/` (AWS SDK config only — no container); `docs/architecture/overview.md` (document the required SNS/SQS resources, same convention as E-01 T08)
**Depends on**: T12
**Reuses**: E-01's AWS dev-account SDK configuration pattern (T07) and dev-account resource documentation pattern (T08)
**Requirement**: MKT-04

**Tools**:
- MCP: `context7` (AWSSDK.SimpleNotificationService / AWSSDK.SQS setup patterns)
- Skill: none

**Done when**:
- [ ] Required SNS topic + SQS subscription documented in `docs/architecture/overview.md` alongside E-01's dev-account resource list
- [ ] `SesFeedbackConsumer` connects and polls successfully against the real dev-account queue on startup
- [ ] `dotnet build` passes

**Tests**: none (infra wiring/docs)
**Gate**: build — `dotnet build --no-incremental`

**Commit**: `chore(infra): configure dev-account SNS/SQS for SES feedback`

---

### T14: `GET /v1/api/unsubscribe` endpoint

**What**: Public, unauthenticated endpoint that verifies the token via `UnsubscribeTokenService` and writes the opt-out through `IConsentRepository`, tagged `source=unsubscribe-link` in the audit log
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Endpoints/Unsubscribe/UnsubscribeEndpoint.cs`
**Depends on**: T06, T07
**Reuses**: `IEndpoint` auto-registration convention (E-01), existing consent audit log (E-05/US-C026)
**Requirement**: MKT-02

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Valid token → 200, `GET /v1/api/consent/{recipientId}` subsequently shows `Marketing: opted-out`
- [ ] Invalid/expired/malformed token → 400 with a clear error, no consent change
- [ ] Replaying the same valid token → 200 again, idempotent, no duplicate audit-log entries when the value doesn't actually change
- [ ] Endpoint requires no authentication (confirmed via test with no auth header)
- [ ] Gate check passes: `dotnet test`
- [ ] Test count: 3+ new tests pass (no silent deletions)

**Tests**: integration
**Gate**: full — `dotnet test`

**Commit**: `feat(api): add public unsubscribe endpoint`

---

### T15: `GET /v1/api/campaigns/{campaignId}` endpoint

**What**: Returns aggregate campaign progress (`sent`, `suppressed`, `failed`, `pending`, `total`) by reading `CampaignSummary` — no per-recipient scan
**Where**: `02-src/01-Api/RentifyxCommunications.Api/Endpoints/Campaigns/CampaignStatusEndpoint.cs`
**Depends on**: T08
**Reuses**: `IEndpoint` convention, same internal-only auth posture as existing status endpoints (E-05)
**Requirement**: MKT-01

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Existing campaign ID → correct aggregate counts returned
- [ ] Unknown campaign ID → 404, not a default-zero response (avoids masking producer typos)
- [ ] Gate check passes: `dotnet test`
- [ ] Test count: 2+ new tests pass (no silent deletions)

**Tests**: integration
**Gate**: full — `dotnet test`

**Commit**: `feat(api): add campaign status endpoint`

---

### T16: Wire campaign hosted services into the API host

**What**: Register `CampaignRequestedConsumer` and `SesFeedbackConsumer` as `IHostedService`s in the same host as the existing `NotificationRequestedConsumer`, confirm graceful start/stop for both alongside the existing consumer (single deployable, ADR-C06 extended)
**Where**: `02-src/04-IoC/RentifyxCommunications.IoC/`, `02-src/01-Api/RentifyxCommunications.Api/Program.cs`
**Depends on**: T05, T12, T09, T08
**Reuses**: Existing `IHostedService` registration pattern (E-01/E-03)
**Requirement**: MKT-01, MKT-04

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] Host starts with all three hosted services (`NotificationRequestedConsumer`, `CampaignRequestedConsumer`, `SesFeedbackConsumer`) running
- [ ] `SIGTERM`/host shutdown drains in-flight work on all three before exit — no abrupt kill
- [ ] `GET /health` reflects all three as healthy
- [ ] Gate check passes: `dotnet test`
- [ ] Test count: 2+ new tests pass (no silent deletions)

**Tests**: integration
**Gate**: full — `dotnet test`

**Commit**: `feat(ioc): wire campaign and feedback hosted services into API host`

---

### T17: Load test — campaign burst vs. steady transactional traffic

**What**: k6 (or equivalent) load test simulating a 1,000-recipient campaign burst concurrently with steady transactional traffic; assert transactional p99 dispatch latency SLO (<5s) holds and zero cross-contamination between the two rate-limit budgets
**Where**: `03-tests/` (load test project or scripts directory, mirroring E-06's US-C032 load test)
**Depends on**: T16, T10, T11
**Reuses**: E-06's load-test tooling and staging-like environment setup, if already established; otherwise this task also stands up the minimal k6 scaffold
**Requirement**: MKT-03

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] 1,000-recipient campaign burst completes with zero duplicate sends (idempotency holds under load)
- [ ] Concurrent transactional traffic's p99 dispatch latency stays under 5s throughout the campaign burst
- [ ] Zero SES `Throttling` errors on either path
- [ ] Results documented in the task's PR/commit description

**Tests**: integration (load)
**Gate**: full — `dotnet test` plus the load test run itself (manual/CI job, not part of the standard unit/integration gate)

**Commit**: `test(load): validate campaign burst isolation from transactional SLO`

---

### T18: Terraform additions — SNS, SQS, SES configuration set, IAM

**What**: Terraform modules for the SNS topic, SQS queue + subscription, SES configuration set with bounce/complaint event destinations pointed at the SNS topic, and IAM permissions for the API's IRSA role to receive from the new SQS queue
**Where**: `iac/` (new modules alongside existing SES/DynamoDB/SecretsManager/IAM modules from E-06)
**Depends on**: T13
**Reuses**: Existing Terraform module structure and IAM least-privilege pattern (E-06)
**Requirement**: MKT-04

**Tools**:
- MCP: `context7` (Terraform AWS provider `aws_sns_topic`, `aws_sqs_queue`, `aws_ses_configuration_set` resource reference)
- Skill: none

**Done when**:
- [ ] `terraform validate` passes
- [ ] `terraform plan` shows the expected new resources with no unintended changes to existing SES/DynamoDB/Secrets modules
- [ ] IAM role is scoped to SQS receive on the specific new queue only, not `sqs:*`

**Tests**: none (IaC)
**Gate**: build — `terraform validate` (from `iac/`)

**Commit**: `feat(iac): add SNS/SQS/SES configuration set for bounce/complaint feedback`

---

### T19: ADRs for E-07 tech decisions

**What**: Write ADR entries for the four non-obvious decisions from design.md: denormalized atomic campaign counters, HMAC-based stateless unsubscribe tokens, SNS/SQS transport for SES feedback (not Kafka), and Kafka topic/consumer-group/rate-limiter isolation for campaigns
**Where**: `docs/decisions/` (new ADR files, numbered after existing C01–C09)
**Depends on**: T17, T18
**Reuses**: Existing ADR template/format from `docs/decisions/` (E-02/E-06)
**Requirement**: MKT-01, MKT-02, MKT-03, MKT-04

**Tools**:
- MCP: none
- Skill: none

**Done when**:
- [ ] 4 new ADRs written, cross-linked from README's "Key architectural decisions" table
- [ ] Each ADR references its corresponding design.md "Tech Decisions" row and the requirement ID(s) it satisfies

**Tests**: none (documentation)
**Gate**: none

**Commit**: `docs: add ADRs for E-07 marketing campaign architecture decisions`

---

## Parallel Execution Map

```
Phase 1 (Sequential):
  T01 ──→ T02 ──→ T03

Phase 2 (Parallel, after T03):
  T03 complete, then:
    ├── T04 ──→ T05
    ├── T06 [P] ──→ T07
    ├── T08 [P]
    └── T12 [P]

Phase 3 (Depends on Phase 2 tracks):
  T01 + T08 ──→ T09
  T04        ──→ T10
  T05        ──→ T11
  T12        ──→ T13

Phase 4 (API & Wiring):
  T06 + T07             ──→ T14
  T08                    ──→ T15
  T05 + T12 + T09 + T08  ──→ T16

Phase 5 (Sequential tail):
  T16 + T10 + T11 ──→ T17
  T13             ──→ T18
  T17 + T18       ──→ T19
```

**Parallelism constraint reminder:** `[P]` tasks in Phase 2 (T04, T06, T08, T12) touch disjoint files (fan-out handler, token service, campaign repository, feedback consumer respectively) and have unit/integration test types already marked parallel-safe or isolated by container fixture — no shared mutable state between them.

---

## Task Granularity Check

| Task | Scope | Status |
| --- | --- | --- |
| T01 | 1 aggregate extension | ✅ Granular |
| T02 | 3 cohesive contracts (model + interface + DTO), same feature slice | ✅ Granular (cohesive) |
| T03 | 1 test file, 2 behaviors | ✅ Granular |
| T04 | 1 handler | ✅ Granular |
| T05 | 1 hosted service | ✅ Granular |
| T06 | 1 service | ✅ Granular |
| T07 | 1 renderer extension + 1 secret registration, same feature slice | ✅ Granular (cohesive) |
| T08 | 1 repository | ✅ Granular |
| T09 | 1 repository extension | ✅ Granular |
| T10 | 1 resilience wrapper instance | ✅ Granular |
| T11 | 1 config change | ✅ Granular |
| T12 | 1 hosted service | ✅ Granular |
| T13 | 1 local infra addition | ✅ Granular |
| T14 | 1 endpoint | ✅ Granular |
| T15 | 1 endpoint | ✅ Granular |
| T16 | 1 wiring change | ✅ Granular |
| T17 | 1 load test | ✅ Granular |
| T18 | 1 Terraform module addition | ✅ Granular |
| T19 | 1 documentation deliverable (4 ADRs, same PR) | ✅ Granular (cohesive) |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows | Status |
| --- | --- | --- | --- |
| T01 | None | None | ✅ Match |
| T02 | T01 | T01→T02 | ✅ Match |
| T03 | T01, T02 | T02→T03 (T01 transitively via T02) | ✅ Match |
| T04 | T02 | T03→T04 | ✅ Match |
| T05 | T04 | T04→T05 | ✅ Match |
| T06 | T03 (phase gate) | T03→T06 [P] | ✅ Match |
| T07 | T06 | T06→T07 | ✅ Match |
| T08 | T02 (phase gate) | T03→T08 [P] | ✅ Match |
| T09 | T01, T08 | T01+T08→T09 | ✅ Match |
| T10 | T04 | T04→T10 | ✅ Match |
| T11 | T05 | T05→T11 | ✅ Match |
| T12 | T02 (phase gate) | T03→T12 [P] | ✅ Match |
| T13 | T12 | T12→T13 | ✅ Match |
| T14 | T06, T07 | T06+T07→T14 | ✅ Match |
| T15 | T08 | T08→T15 | ✅ Match |
| T16 | T05, T12, T09, T08 | T05+T12+T09+T08→T16 | ✅ Match |
| T17 | T16, T10, T11 | T16+T10+T11→T17 | ✅ Match |
| T18 | T13 | T13→T18 | ✅ Match |
| T19 | T17, T18 | T17+T18→T19 | ✅ Match |

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires (this project's convention) | Task Says | Status |
| --- | --- | --- | --- | --- |
| T01 | Domain entity extension | none (structural) | none | ✅ OK |
| T02 | Domain contracts | none (contracts only) | none | ✅ OK |
| T03 | Domain test | unit | unit | ✅ OK |
| T04 | Application handler | unit | unit | ✅ OK |
| T05 | API hosted service (consumer) | unit | unit | ✅ OK |
| T06 | Application service | unit | unit | ✅ OK |
| T07 | Infrastructure (renderer + secrets) | unit | unit | ✅ OK |
| T08 | Infrastructure repository | integration | integration | ✅ OK |
| T09 | Infrastructure repository extension | integration | integration | ✅ OK |
| T10 | Infrastructure resilience wrapper | unit | unit | ✅ OK |
| T11 | Config/wiring | none | none | ✅ OK |
| T12 | API hosted service (consumer) | unit | unit | ✅ OK |
| T13 | Local infra wiring | none | none | ✅ OK |
| T14 | API endpoint | integration | integration | ✅ OK |
| T15 | API endpoint | integration | integration | ✅ OK |
| T16 | IoC wiring | integration | integration | ✅ OK |
| T17 | Load test | integration (load) | integration | ✅ OK |
| T18 | IaC | none | none | ✅ OK |
| T19 | Documentation | none | none | ✅ OK |

All checks pass — no restructuring needed before presenting.
