# E-07 · Marketing Email Campaigns — Specification

## Problem Statement

v1 (E-01–E-06) covers transactional, single-recipient email only (`NotificationRequested` — one event, one recipient, triggered by a domain fact like an asset approval). Marketing needs a second, structurally different flow: one trigger fans out to many recipients, has a different consent channel (`Marketing`, opt-in by default is prohibited under LGPD Art. 8 — opt-out only, never assumed), needs public unsubscribe, and cares about sender reputation (bounce/complaint) in a way transactional mail doesn't. Bolting this onto the existing single-recipient dispatch path risks corrupting the transactional SLOs (>99% send success, p99 <5s) that E-04 was built to protect. E-07 adds campaign email as a distinct, additive flow that reuses the proven building blocks (SES sender, template renderer, consent repository, outbox/idempotency, rate limiter) without touching their existing behavior.

## Goals

- [ ] A producer service can publish a `CampaignRequested` event with a pre-resolved recipient list and have every eligible recipient emailed, respecting per-recipient `Marketing` consent
- [ ] Every marketing email includes a working, no-login unsubscribe link that immediately opts the recipient out of the `Marketing` channel
- [ ] Campaign sends never starve or degrade transactional dispatch (isolated rate-limit budget, isolated Kafka topic/consumer group)
- [ ] Bounce/complaint feedback is captured and folded into consent (hard bounce / complaint ⇒ auto opt-out) — closes the gap flagged as backlog in v1's Watch Items

## Out of Scope

| Feature | Reason |
| --- | --- |
| Campaign creation/management UI or admin API | No admin tool exists yet; producers publish events directly (see Context) |
| Segmentation logic (who belongs in a campaign) | Owned by the publishing service; this service only takes a resolved `recipientIds` list |
| A/B testing, send-time optimization, analytics dashboards | Not needed for v1 of this epic; revisit if a real campaign volume materializes |
| SMS/push marketing | Same v1 boundary as transactional — email only |
| Real-time campaign progress UI | Status is queryable via existing per-notification endpoints; no aggregate campaign dashboard yet |

---

## User Stories

### P1: MKT-01 — Campaign Fan-Out ⭐ MVP

**User Story**: As a producing service, I want to publish one `CampaignRequested` event with a recipient list and template, and have the platform send an individual email to every eligible recipient.

**Why P1**: This is the core capability — without fan-out, there's no campaign feature, just more transactional email.

**Acceptance Criteria**:

1. WHEN a `CampaignRequested` event arrives with `campaignId`, `templateId`, `recipientIds[]`, `payload` THEN the system SHALL create one `Notification` record per recipient with `Channel = Marketing` and `category = Campaign`
2. WHEN fan-out creates per-recipient notifications THEN each SHALL carry its own idempotency key derived from `campaignId + recipientId` (not the campaign-level `correlationId` alone) so a replayed campaign event doesn't re-send to anyone
3. WHEN a recipient in the list has opted out of `Marketing` THEN their notification SHALL be created with status `Suppressed` and SES SHALL NOT be called for them — same consent gate as transactional (ADR-C04), reused, not reimplemented
4. WHEN fan-out is in progress THEN individual recipient failures (bad address, render error) SHALL NOT block or fail the rest of the campaign
5. WHEN a producer queries campaign progress THEN it SHALL be able to do so via `GET /v1/api/campaigns/{campaignId}` returning aggregate counts (sent/suppressed/failed/pending)

**Independent Test**: Publish a `CampaignRequested` event with 5 recipient IDs (2 opted out) against LocalStack; confirm 3 SES sends, 2 suppressed records, and `GET /v1/api/campaigns/{campaignId}` reports `{sent:3, suppressed:2, failed:0}`.

---

### P1: MKT-02 — Public Unsubscribe ⭐ MVP

**User Story**: As an email recipient, I want a one-click unsubscribe link in every marketing email that works without logging in, so I can stop receiving campaign email immediately.

**Why P1**: LGPD Art. 8 requires opt-out to be as easy as opt-in was. A login-gated unsubscribe is a compliance risk, not just a UX gap.

**Acceptance Criteria**:

1. WHEN a `Marketing`-channel email is rendered THEN the template SHALL include an unsubscribe link containing a signed, short-lived, single-purpose token (recipientId + channel, not a general-purpose auth token)
2. WHEN `GET /v1/api/unsubscribe?token=...` is called with a valid token THEN the system SHALL set `ConsentPreference(Marketing, OptedIn=false)` and return a plain confirmation page/response — no authentication required
3. WHEN the token is expired, malformed, or already used past its validity window THEN the system SHALL return a clear error without opting anyone out silently
4. WHEN an unsubscribe token is replayed after successful use THEN the system SHALL treat it as idempotent (still opted out, no error) — a recipient double-clicking shouldn't see a failure
5. WHEN a recipient unsubscribes THEN the same consent audit log used for `PUT /v1/api/consent` (US-C026) SHALL record the change, tagged with source `unsubscribe-link`

**Independent Test**: Send a campaign email via LocalStack SES mock, extract the unsubscribe token from the rendered body, call the endpoint, confirm `GET /v1/api/consent/{recipientId}` now shows `Marketing: opted-out`, and confirm replaying the same token doesn't error.

---

### P2: MKT-03 — Isolated Throughput Budget

**User Story**: As an SRE, I want campaign sends to have their own rate-limit and consumer group so a large campaign never delays transactional email.

**Why P2**: Important for production safety once campaigns exist, but the system is safe to ship MKT-01/02 first against a shared (conservatively sized) budget and split the limiter afterward — this is a hardening story, not a blocker for the core capability.

**Acceptance Criteria**:

1. WHEN campaign and transactional events arrive concurrently THEN they SHALL be consumed from separate Kafka topics (`campaign-requested` vs `notification-requested`) with separate consumer groups
2. WHEN the campaign consumer sends via SES THEN it SHALL draw from a token bucket separate from the transactional bucket, both sized within the shared account SES quota (see existing Watch Item on shared quota)
3. WHEN the transactional bucket is healthy but the campaign bucket is exhausted THEN transactional sends SHALL be unaffected

**Independent Test**: Load-test a 1,000-recipient campaign burst concurrently with steady transactional traffic; confirm transactional p99 dispatch latency SLO (<5s) holds throughout.

---

### P2: MKT-04 — Bounce/Complaint Feedback Loop

**User Story**: As an LGPD/compliance owner and as an SRE protecting sender reputation, I want hard bounces and spam complaints to automatically opt the recipient out of `Marketing`.

**Why P2**: Not required for the first campaign to go out, but was already flagged as a v1.1 backlog item in the original plan — E-07 is the natural place to close it since campaign volume is what makes reputation risk real.

**Acceptance Criteria**:

1. WHEN SES SNS feedback (bounce or complaint notification) arrives for a `Marketing` send THEN the system SHALL set `ConsentPreference(Marketing, OptedIn=false)` for that recipient
2. WHEN the feedback is a *soft* bounce THEN the system SHALL NOT auto opt-out — only hard bounce and complaint trigger suppression
3. WHEN a transactional-channel recipient hard-bounces THEN the same suppression rule SHALL apply per-channel (opting out `Marketing` doesn't opt out `Transactional`, and vice versa)

**Independent Test**: Inject a mocked SES bounce notification for a test recipient; confirm their `Marketing` consent flips to opted-out and a subsequent campaign skips them.

---

## Edge Cases

- WHEN a `CampaignRequested` event lists a `recipientId` with no known consent record THEN the system SHALL treat it as opted-out (LGPD default is deny, never assume opt-in)
- WHEN a campaign's `recipientIds[]` is empty THEN the system SHALL ack the event, create zero notifications, and report `{sent:0, suppressed:0, failed:0}` — not an error
- WHEN a campaign event exceeds a sane size ceiling (e.g., >50,000 recipients in one event) THEN the system SHALL reject it at the consumer boundary with a clear log — a producer should paginate, not send unbounded lists
- WHEN the same `campaignId` is published twice (rebalance/retry) THEN per-recipient idempotency (Acceptance Criterion MKT-01.2) SHALL prevent duplicate sends without needing a campaign-level lock

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| --- | --- | --- | --- |
| MKT-01 | P1: Campaign Fan-Out | Implementing | In Tasks (T01–T05, T08, T09, T15, T16) |
| MKT-02 | P1: Public Unsubscribe | Implementing | In Tasks (T06, T07, T14) |
| MKT-03 | P2: Isolated Throughput Budget | Implementing | In Tasks (T10, T11, T17) |
| MKT-04 | P2: Bounce/Complaint Feedback Loop | Implementing | In Tasks (T12, T13, T18) |

**Coverage:** 4 total, 4 mapped to tasks, 0 unmapped ✅ (see `.specs/features/e07-marketing-campaigns/tasks.md`, T01–T19)

---

## Success Criteria

- [ ] A campaign to N recipients results in exactly N notification records, no duplicates, correct consent enforcement
- [ ] Unsubscribe works with zero authentication and takes effect on the very next campaign send
- [ ] Transactional SLOs (>99% success, p99 <5s) hold unchanged during a concurrent campaign burst
- [ ] Zero PII retained beyond the existing 90-day TTL; unsubscribe tokens are single-purpose and cannot be reused as general auth
