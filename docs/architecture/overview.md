# Architecture Overview

## Target Architecture (Ideal)

```mermaid
flowchart TB
    subgraph Producers["Event Producers"]
        IdentityAPI["identity-api"]
        AssetRegistry["asset-registry"]
        LeasingAPI["leasing-api"]
    end

    Producers -->|"NotificationRequested"| Topic[["Kafka topic:<br/>notification-requested"]]

    subgraph Host["RentifyX.Communications.Api — single deployable (ADR-C06)"]
        Consumer["NotificationRequestedConsumer<br/>(IHostedService)"]
        Handler["DispatchNotificationHandler"]
        Idempotency["SaveIfNotExists<br/>(conditional write, ADR-C08)"]
        Consent{"IConsentRepository<br/>LGPD Art. 8 check"}
        Renderer["ITemplateRenderer<br/>(Scriban)"]
        Limiter["ResilientEmailSender<br/>token-bucket limiter + Polly circuit breaker (ADR-C09, F-08)"]
        Sender["IEmailSender<br/>(SesEmailSender / MockEmailSender,<br/>selected via IHostEnvironment)"]
        Secrets["ISecretsProvider"]

        Topic --> Consumer --> Handler
        Handler --> Idempotency --> Consent
        Consent -->|opted-in| Renderer --> Limiter --> Sender
        Consent -->|opted-out| Suppressed["status = Suppressed<br/>(SES never called)"]
    end

    subgraph AWS["Real AWS account (dev/sandbox locally — AD-012; same account family in staging/prod)"]
        DynamoDB[("DynamoDB<br/>notifications + delivery-log")]
        SES[("SES")]
        SecretsManager[("Secrets Manager")]
        KMS[("KMS")]
    end

    Idempotency -.-> DynamoDB
    Consent -.-> DynamoDB
    Sender --> SES
    Secrets -.->|SES ARN, Kafka SASL creds| SecretsManager
    SecretsManager -.-> KMS
    Host -.->|resolves secrets at startup| Secrets

    subgraph Obs["Observability"]
        OTEL["OpenTelemetry SDK"] --> Collector["OTEL Collector"]
        Collector --> Dash["Aspire Dashboard (local) /<br/>APM backend (staging/prod)"]
    end

    Host -.-> OTEL
```

**Key properties of the target design:**

- The Kafka consumer runs **inside** the API host as an `IHostedService` (ADR-C06) — one deployable, shared health checks, no separate consumer process to operate.
- Every dispatch follows the outbox lifecycle (`Pending → Rendering → Dispatching → Sent | Failed | Suppressed`, ADR-C07) so a crash mid-send can be reconciled without duplicate emails.
- Consent is checked **inside** this service, never trusted from the producer (ADR-C04) — centralizes LGPD Art. 8 compliance in one place.
- `ISecretsProvider` is the same abstraction and code path in every environment (local dev, staging, prod) — only the underlying AWS account/credentials differ (AD-012).
- `ResilientEmailSender` (F-08, 2026-07-14) wraps `IEmailSender` with a Polly `ResiliencePipeline` — a token-bucket rate limiter and a ratio-based circuit breaker (`FailureRatio = 1.0` + `MinimumThroughput`, approximating "N consecutive SES failures" since Polly v8 dropped v7's pure consecutive-count breaker) — both thresholds configuration-driven via `ResilienceOptions`. A send rejected by either (queue-wait timeout or open circuit) is mapped to an `ErrorOr` failure and the notification is marked `Failed`; routing that failure to a DLQ is F-09's concern, not F-08's. `ResilienceStartupValidator` fails fast at startup if any threshold is misconfigured (mirrors `SecretsStartupValidator`).

## Environments

| Environment | AWS access | Kafka | Notes |
|---|---|---|---|
| Local dev (manual `dotnet run --project AppHost`) | Real AWS dev/sandbox account via a named credentials profile (`AWS:Profile` config key) | Local container via Aspire AppHost | No LocalStack (AD-012, 2026-07-11) — dev-account resources (tables, SES identity, secrets) must exist before running; see "AWS Dev Account Requirements" below |
| Automated tests (unit + integration, local and CI) | LocalStack container (Testcontainers) — no real AWS credentials needed | Testcontainers Kafka | AD-013, 2026-07-12 — LocalStack is used for automated test runs only, never for manual dev/AppHost sessions; this also means CI needs no real AWS credentials to run the suite |
| **Exception:** `AppHostTests` (`03-tests/05-Integration`) | Boots the real API process via `DistributedApplicationTestingBuilder` — **not** LocalStack, hits T07's fail-fast `AWS:Profile` check like a manual run | Local container via Aspire AppHost | Discovered 2026-07-12 while verifying T09: requires `dotnet user-secrets set "AWS:Profile" "<profile>" --project 02-src/01-Api/RentifyxCommunications.Api` locally or the suite times out waiting for `/health`. Not yet resolved for CI (no real AWS profile there) — tracked as an open item in `.specs/project/STATE.md` Todos |
| Staging / Production | IRSA (IAM Roles for Service Accounts) — no static credentials on the pod | Managed Kafka cluster | Provisioned via Terraform (`iac/`), deployed via Helm (`k8s/`) |

## AWS Dev Account Requirements

These resources are **not** auto-provisioned by this service — they must exist in the dev/sandbox account before the app can run end-to-end via `dotnet run --project AppHost` (manually today; via the E-06 Terraform module once that lands). They are **not** needed to run the automated test suite — those tests use a LocalStack container instead (AD-013):

| Resource | Detail |
|---|---|
| DynamoDB table `notifications` | PK = `NOTIF#{id}` (S), billing mode `PAY_PER_REQUEST` |
| DynamoDB table `delivery-log` | PK = `LOG#{id}` (S), billing mode `PAY_PER_REQUEST` |
| SES sender identity | A verified domain or email identity for outbound sends |
| Secrets Manager entries | `rentifyx/comms/ses-arn`, `rentifyx/comms/kafka-sasl-username`, `rentifyx/comms/kafka-sasl-password` |

## Resilience Configuration (F-08)

`ResilienceOptions` binds from the `Resilience` configuration section (falls back to the defaults below if the section is absent). **These are conservative placeholder values, not the confirmed real SES account sending-rate quota** — B-001 (`.specs/project/STATE.md` Active Blockers) is still open. Retune via configuration once the real quota is known; no code change or redeploy is required.

| Setting | Default | Purpose |
|---|---|---|
| `Resilience:TokenBucketPermitsPerSecond` | `14` | Token-bucket refill rate — sends per second allowed before throttling |
| `Resilience:TokenBucketQueueMaxWaitSeconds` | `5` | How long a send may wait for a permit before being rejected (approximated as queue depth, not a literal wall-clock timeout — the BCL rate limiter has no such knob) |
| `Resilience:CircuitBreakerMinimumThroughput` | `5` | Failures required within the sampling window to open the circuit (approximates "N consecutive failures") |
| `Resilience:CircuitBreakerSamplingDurationSeconds` | `30` | Window `MinimumThroughput` failures must occur within |
| `Resilience:CircuitBreakerBreakDurationSeconds` | `30` | How long the circuit stays open before allowing a half-open probe |

## Layer Structure

```
01-Api              → Endpoints, Extensions, OpenAPI config
02-Application      → Handlers, Validators, DTOs, Mappers
03-Domain           → Entities, Interfaces, Filters, Constants, Resources
04-IoC              → Dependency injection registrations
05-Infrastructure   → Repository implementations, persistence
```

## Dependency Flow

```
Api → Application → Domain ← Infrastructure
            ↑
           IoC (wires all layers)
```

- **Domain** has no outbound dependencies — it defines interfaces, not implementations.
- **Infrastructure** implements Domain interfaces (repositories).
- **Application** depends only on Domain interfaces, never on Infrastructure directly.
- **IoC** is the only layer that references all others.
- **Api** depends on Application (handlers) and IoC.

## Repository Pattern

Repositories use segregated interfaces — one per operation — combined into a feature-specific composite:

```
Domain/Interfaces/Common/     IAddRepository<T>, IGetByIdRepository<T>,
                              IUpdateRepository<T>, IDeleteRepository<T>,
                              IGetAllRepository<T, TFilter>

Domain/Interfaces/<Feature>/  I<Feature>Repository (implements all of the above)

Domain/Filters/<Feature>/     <Feature>Filter (pagination + search criteria)
```

Handlers inject `I<Feature>Repository` directly. The HTTP request is mapped to the filter in the feature mapper (`ToFilter`) before being passed to the repository. Registration is explicit in `InfrastructureDependencyInjection.cs`.

## Feature Organization

Each feature lives under `Features/<FeatureName>/Handlers/<Operation>/` in the Application layer and `Endpoints/<FeatureName>/` in the Api layer. All files for one operation are co-located.

## Test Projects

| Project | What it tests |
|---|---|
| Tests.Validators | FluentValidation rules in isolation |
| Tests.Handlers | Handler logic with mocked repositories |
| Tests.Repositories | Repository queries against a real in-memory DB |
| Tests.Integration | Full HTTP pipeline via WebApplicationFactory |
| Tests.Common | Shared builders used by the above |
