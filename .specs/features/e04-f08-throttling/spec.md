# E-04 · F-08 — Throttling & Circuit Breaking Specification

## Problem Statement

F-07 shipped `SesEmailSender`/`MockEmailSender` as direct, unprotected adapters — every dispatch calls SES with no guard against exceeding the account's sending-rate quota or against hammering a degraded SES endpoint. Under a burst (e.g. bulk asset approvals fanning out notifications), this risks self-inflicted `Throttling` errors and wasted retries against an endpoint that's already failing. F-08 adds a resilience layer in front of `IEmailSender` so the system self-limits before AWS does.

## Goals

- [ ] Outbound SES calls are throttled by a token-bucket rate limiter sized below the account's actual sending quota, so bursts never trigger AWS `Throttling` errors
- [ ] A circuit breaker opens after sustained SES failures, so a degraded SES endpoint is not hammered with doomed retries
- [ ] Both thresholds are configuration-driven (no code change needed once the real SES quota — B-001 — is confirmed)

## Out of Scope

| Feature | Reason |
| --- | --- |
| DLQ routing for failed/circuit-broken sends | F-09's concern. F-08 marks a notification `Failed` (existing `NotificationStatus`, via `UpdateStatusAsync`) — actually routing that failure to a DLQ topic happens once F-09 builds the DLQ chain. Confirmed with the user 2026-07-14. |
| Retry with exponential backoff for transient errors | F-09's concern — F-08 only decides "let it through" vs. "reject fast" (rate limiter / circuit breaker), not "try again later" |
| Confirming the real AWS SES sending-rate quota | Tracked separately as active blocker B-001 (`.specs/project/STATE.md`) — F-08 ships with a conservative configurable placeholder, not blocked on the real number (confirmed with the user 2026-07-14) |
| Consumer lag / dispatch-duration OTEL metrics | F-09's concern per ROADMAP.md |
| Reconciliation job for notifications stuck in `Dispatching` | F-09's concern per ROADMAP.md |

---

## User Stories

### P1: Token-bucket rate limiting in front of SES ⭐ MVP

**User Story**: As the system, I want outbound SES calls throttled by a token bucket sized below the account's sending-rate quota, so that a burst of notifications never triggers an AWS SES `Throttling` error.

**Why P1**: This is the core self-protection mechanism F-08 exists to add — without it, F-07's adapters have no defense against exceeding SES's account-level rate limit.

**Acceptance Criteria**:

1. WHEN a send is attempted and a token is available THEN the system SHALL consume one token and call SES immediately
2. WHEN a send is attempted and no token is available THEN the system SHALL wait in a bounded queue until a token becomes available or a configured max-wait timeout elapses
3. WHEN the max-wait timeout elapses before a token becomes available THEN the system SHALL treat the send as failed and mark the notification `Failed` (same handling as an SES failure — see P2) without calling SES
4. WHEN the token-bucket rate is changed in configuration THEN the new rate SHALL take effect on the next application start (no code change required)

**Independent Test**: Fire more sends per second than the configured token rate in a test and assert SES is never called more often than the configured rate allows, and that overflow sends beyond the queue's max wait are marked `Failed`.

---

### P1: Circuit breaker around SES calls ⭐ MVP

**User Story**: As the system, I want a circuit breaker that opens when SES calls are consistently failing, so that a degraded SES endpoint isn't hammered with sends that are very likely to fail too.

**Why P1**: Without a circuit breaker, a real SES outage means every queued notification still pays the full SES round-trip (and its failure latency) one at a time — the breaker fails fast instead.

**Correction (2026-07-14, Design):** Polly v8 (the current major version, no "v7-compatible" mode available) removed the pure consecutive-failure-count circuit breaker from v7 (`exceptionsAllowedBeforeBreaking`). Its replacement, `AddCircuitBreaker(CircuitBreakerStrategyOptions)`, is ratio-based: `FailureRatio` + `SamplingDuration` + `MinimumThroughput`. Confirmed via Context7 (`app-vnext/polly` docs). To approximate "N consecutive failures," this feature configures `FailureRatio = 1.0` (100% of calls in the window must fail) with `MinimumThroughput` set to the configured threshold (5) over a configured `SamplingDuration` — any success within the window resets the ratio below 1.0 and prevents opening. This is not bit-for-bit identical to "the last 5 calls in a row failed" (a success far outside a short sampling window wouldn't count against the streak either way, but calls spaced out near the window boundary could behave slightly differently) — close enough for this feature's purpose, and confirmed with the user as the accepted approximation rather than hand-rolling a custom consecutive-failure counter.

**Acceptance Criteria**:

1. WHEN SES calls fail continuously such that the configured minimum-throughput count of calls all fail within the configured sampling window THEN the circuit SHALL open (`FailureRatio = 1.0` reached) and subsequent send attempts SHALL fail fast (no SES call attempted) until the breaker's configured break duration elapses
2. WHEN the circuit is open and a send is attempted THEN the notification SHALL be marked `Failed` without calling SES
3. WHEN the breaker's break duration elapses THEN the circuit SHALL transition to half-open and allow a limited number of probe sends through
4. WHEN a probe send succeeds while half-open THEN the circuit SHALL close and normal sending SHALL resume
5. WHEN a probe send fails while half-open THEN the circuit SHALL re-open for another full break duration
6. WHEN the minimum-throughput threshold, sampling duration, or break duration is changed in configuration THEN the new values SHALL take effect on the next application start (no code change required)

**Independent Test**: Force enough consecutive `SesEmailSender` failures within the sampling window in a test (mocked `IAmazonSimpleEmailService` throwing) to reach the minimum-throughput threshold, and assert the next attempt never invokes the underlying client while the circuit is open.

---

### P2: Configuration-driven thresholds ready for the real SES quota

**User Story**: As an operator, I want the token-bucket rate and circuit-breaker threshold configurable via `appsettings`/environment, so I can retune them the moment the real SES account quota (B-001) is confirmed, without a code deployment.

**Why P2**: Not required for F-08 to function correctly today (the placeholder is safe), but this is the mechanism that actually resolves B-001 once the real number is known — worth calling out as its own story since it's the reason the placeholder is acceptable.

**Acceptance Criteria**:

1. WHEN `ResilienceOptions` (or equivalent) is bound from configuration THEN it SHALL expose the token-bucket rate (permits/second), the queue max-wait duration, the circuit-breaker consecutive-failure threshold, and the circuit-breaker break duration as named, documented settings — never magic numbers inlined in code (per CLAUDE.md)
2. WHEN no explicit configuration is supplied THEN the system SHALL default to the conservative placeholder values below

**Independent Test**: Override one setting via `appsettings.Development.json` and confirm the resilience pipeline picks it up without a code change.

---

### P3: Burst load test evidence

**User Story**: As an operator, I want a load test proving 1,000 notifications fired in a burst produce zero SES `Throttling` errors, so I have concrete evidence the rate limiter holds under the target load profile from AD-009.

**Why P3**: Valuable confidence-building evidence, not required to ship F-08's actual protection — the unit/integration tests already prove the rate limiter and breaker behave correctly at the mechanism level.

**Acceptance Criteria**:

1. WHEN 1,000 notifications are fired in a burst against `MockEmailSender` (configured to simulate SES `Throttling` above the configured rate) THEN the resilience pipeline SHALL ensure the simulated SES call rate never exceeds the configured token-bucket rate

**Independent Test**: Manual/on-demand load test script or test, not part of the default CI gate (documented as such in `tasks.md` when this phase reaches Tasks).

---

## Edge Cases

- WHEN `SaveIfNotExistsAsync` already persisted the notification (per F-07's outbox lifecycle) and the resilience layer then rejects the send (rate-limit timeout or open circuit) THEN the notification SHALL still be marked `Failed` via `UpdateStatusAsync` — it must never remain stuck in `Dispatching` (that reconciliation is F-09's job, but F-08 must not make it worse by leaving records perpetually stuck when the failure is already known synchronously)
- WHEN the circuit breaker is open AND the token bucket also has no available tokens THEN the circuit-breaker check SHALL short-circuit first (cheaper check, avoids queueing sends that are going to be rejected anyway)
- WHEN `MockEmailSender` is the active `IEmailSender` (per F-07's `IHostEnvironment`-gated DI switch) THEN the resilience layer SHALL still wrap it — non-Production environments exercise the same rate-limit/circuit-breaker code path, just against the mock, so behavior isn't only tested in Production
- WHEN the configured token-bucket rate or circuit-breaker threshold is zero or negative THEN the system SHALL fail fast at startup (fail-fast validation, mirroring `SecretsStartupValidator`'s precedent) rather than silently misbehaving at runtime

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| THR-01 | P1: Token-bucket rate limiting | In Tasks | Pending — T03, T04 |
| THR-02 | P1: Token-bucket rate limiting | In Tasks | Pending — T03, T04 |
| THR-03 | P1: Token-bucket rate limiting | In Tasks | Pending — T04 |
| THR-04 | P1: Circuit breaker | In Tasks | Pending — T03, T04 |
| THR-05 | P1: Circuit breaker | In Tasks | Pending — T04 |
| THR-06 | P1: Circuit breaker | In Tasks | Pending — T03 |
| THR-07 | P2: Configuration-driven thresholds | In Tasks | Pending — T02, T03, T06 |
| THR-08 | P3: Burst load test evidence | In Tasks | Pending — T07 |

**ID format:** `THR-[NUMBER]`

**Status values:** Pending → In Design → In Tasks → Implementing → Verified

**Coverage:** 8 total, 8 to be mapped in Design/Tasks, 0 unmapped

---

## Success Criteria

- [ ] A burst of sends beyond the configured token-bucket rate never results in more SES calls per second than configured
- [ ] 5 consecutive SES failures open the circuit; the 6th attempt never reaches the underlying SES client while open
- [ ] A notification rejected by the rate limiter (timeout) or the circuit breaker (open) is marked `Failed`, never left stuck in `Dispatching`
- [ ] All thresholds are configuration-bound with named settings, no magic numbers, and fail fast at startup if misconfigured
- [ ] `dotnet run --project AppHost` boots cleanly with the resilience-wrapped `IEmailSender` registered, no DI resolution errors

---

## Configuration Defaults (placeholder, pending B-001)

| Setting | Default | Source |
| --- | --- | --- |
| Token-bucket rate | 14 permits/second | STATE.md B-001 workaround value (2026-07-03), reused here per 2026-07-14 decision — **not the real confirmed SES quota** |
| Token-bucket queue max-wait | 5 seconds | New for F-08 — conservative, keeps a single stalled consumer message from blocking indefinitely |
| Circuit-breaker minimum-throughput (approximates "consecutive failures") | 5 | Confirmed with the user 2026-07-14; see the Circuit breaker story's Design correction for why this isn't a literal consecutive-count |
| Circuit-breaker sampling duration | 30 seconds | New for F-08 — the window `MinimumThroughput` failures must occur within for `FailureRatio = 1.0` to trigger |
| Circuit-breaker break duration | 30 seconds | New for F-08 — placeholder, revisit alongside B-001 resolution |

These are starting points for Design, not final tuning — flagged as configuration, not hardcoded, specifically so they can change without a redeploy once B-001 resolves.
