# `NotificationRequested` Kafka Contract

**Topic**: `notification-requested`
**Consumer group**: `rentifyx-communications-api` (see `Kafka:ConsumerGroupId` in `appsettings.json`)
**Direction**: Producers (e.g. `rentifyx-identity-api`, `rentifyx-leasing-api`) publish; `communications-api` consumes only. There is no synchronous HTTP intake for notification dispatch (ADR-C01) — producers only receive acknowledgement that the event was accepted onto the topic, not that the email was actually sent. Query delivery status afterward via `GET /v1/api/notifications/{id}` or `GET /v1/api/notifications/recipient/{recipientId}` (E-05 F-10).

## Message Schema

```json
{
  "correlationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "recipientId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "recipientEmail": "user@example.com",
  "channel": "Email",
  "templateId": "password-reset",
  "payload": {
    "resetLink": "https://app.rentifyx.com/reset?token=abc123",
    "expiresInMinutes": "30"
  }
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `correlationId` | `Guid` | Yes | **Idempotency key** (AD-008). Re-publishing the same `correlationId` (e.g. on consumer redelivery) is a no-op — the duplicate write is rejected at the DynamoDB layer and the message is acked without reprocessing. Producers should generate one `correlationId` per logical notification attempt, not per Kafka publish retry. |
| `recipientId` | `Guid` | Yes | Used for consent lookup (`IConsentRepository`) and notification history queries (`GET /notifications/recipient/{recipientId}`). |
| `recipientEmail` | `string` | Yes | Destination address for the `Email` channel. Ignored (but still required by the schema) for channels without an email-shaped destination once those ship. |
| `channel` | `string` | Yes | One of the `Channel` enum's member names, as a string — never persist or transmit the numeric value (see Enum Persistence in the root `CLAUDE.md`). See **Channel values** below. |
| `templateId` | `string` | Yes | Resolved by `ITemplateRenderer`/`ScribanTemplateRenderer`. An unknown `templateId` is classified as a `PoisonPill` failure (routed straight to DLQ, not retried) — it is treated as a deployment defect, not a transient error. |
| `payload` | `Dictionary<string, string>` | Yes (may be empty) | Template variables, all string-typed. Not validated against the template's expected variable set before rendering — a missing key surfaces as a template-rendering failure. |

## `channel` values (AD-002)

The `Channel` enum is defined with all planned values from day one, even though only `Email` has real dispatch logic implemented as of this document:

| Value | Status |
|---|---|
| `Email` | Implemented — full pipeline (consent check, template render, SES send, retry/DLQ, reconciliation). |
| `Sms` | Reserved, not implemented. A `NotificationRequested` event with `channel: "Sms"` is accepted onto the topic (schema is channel-agnostic) but has no dispatch handler wired yet. |
| `Push` | Reserved, not implemented. Same as `Sms`. |

This is deliberate: adding a real channel later does not require a breaking schema change, only a new dispatch implementation behind the existing `channel` field.

## Consent semantics (AD-013, AD-014)

Before dispatch, `communications-api` independently checks `IConsentRepository` for `(recipientId, channel)` — it never trusts a producer's implicit claim that consent exists (ADR-C04). For the `Email` channel specifically (transactional), **absence of a consent record is NOT suppression** — dispatch proceeds unless a record explicitly says `optedIn: false`. This is the opposite default from the Marketing channel (E-07, opt-out/default-deny) — do not assume the two channels share a suppression default.

## Delivery guarantees

- **At-least-once, deduplicated by `correlationId`.** A crash between "notification persisted as `Pending`" and "SES confirms send" is recovered by the reconciliation sweep (`ReconciliationHostedService`, polls `GSI3`/`STATUS#Dispatching`), not by re-delivery from the producer.
- **No synchronous send confirmation.** Publish success only means the event was accepted onto the topic. Query the status endpoints for actual delivery outcome.
- **Retry/DLQ**: transient failures (SES throttling, circuit-breaker open) are retried via a delay-chain (`notification-requested-retry-5s` → `-1m` → `-10m` → dlq). Poison-pill failures (malformed JSON, unknown `templateId`) go straight to DLQ, no retry.

## Related endpoints (E-05 F-10)

| Endpoint | Purpose |
|---|---|
| `GET /v1/api/notifications/{id}` | Look up a single notification's current status by id. |
| `GET /v1/api/notifications/recipient/{recipientId}` | List a recipient's notification history. |
| `GET /v1/api/consent/{recipientId}?channel={channel}` | Read current consent state before publishing, instead of relying on stale local state. |
| `PUT /v1/api/consent/{recipientId}` | Update consent (audited per LGPD Art. 8). |

All four require an `X-Api-Key` header (service-to-service auth, no identity-api/JWT coupling this cycle — AD-011).
