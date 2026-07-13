# Handoff

**Date:** 2026-07-13T20:30:00Z
**Feature:** E-01 · Project Foundation & DevSecOps Pipeline
**Task:** All done — E-01 milestone is complete. T16 (branch protection) applied 2026-07-13T20:30:00Z.

## Completed ✓

T01–T15 and T17 are all done, and the branch `feat/aws-dev-account-config` was merged to `main` via PR #2 on 2026-07-13T20:03:40Z:

- T01–T08: scaffold, build props, `.editorconfig`, Aspire AppHost+ServiceDefaults, Serilog/CorrelationId/health/Scalar/ErrorOr, `GlobalExceptionHandler`, AWS SDK against real dev/sandbox account (fail-fast), dev-account resource docs
- T09: Kafka container (KRaft mode) in AppHost
- T10: `NotificationRequestedConsumer` `IHostedService` skeleton (no message processing yet — that's E-03)
- T11: `ISecretsProvider`/`SecretsProviderOptions` in Application layer
- T12: `SecretsManagerProvider` + `SecretsStartupValidator`, fail-fast at startup, verified against LocalStack
- T13: CI workflow — build, test, 80% coverage gate
- T14: Dockerfile hardened to non-root + Trivy image scan in CI (found and fixed a real HIGH CVE, `Microsoft.OpenApi` CVE-2026-49451, along the way)
- T15: OWASP Dependency-Check wired into CI (via the CLI Docker image directly, not the stale `Dependency-Check_Action` wrapper)
- T17: git-secrets wired into `.hooks/pre-commit`

## Pending

- Nothing — E-01 as a milestone is complete. E-02 (Domain Model — Notification & Consent) is next per `.specs/project/ROADMAP.md`.

## Known gaps (all previously surfaced to and accepted by the user — not oversights)

- `AppHostTests`' health-check test fails locally/would fail in CI for two independent reasons: (1) needs `AWS:Profile` via user-secrets, not set up in CI; (2) needs 3 real secrets provisioned in the AWS dev/sandbox account's Secrets Manager, never done (T08 scoped that as manual). It's tagged `Category=Integration` and excluded from the CI test filter, so it doesn't break CI — it's just not exercised there. `AppHost_StartsKafkaResource_AndBrokerIsReachable` is unaffected by both.
- CI's 80% coverage gate is real and will be red on the first push — actual repo coverage is ~5.6% (E-01 is foundational scaffolding; the `Examples` feature is template boilerplate with skipped placeholder tests). Intentional, not a bug.
- T15's `owasp-check` CI job needs an `NVD_API_KEY` repository secret added (README documents how) before it can actually run — not set up.
- `.github/workflows/ci.yml` pins `aquasecurity/trivy-action` to a commit SHA (post- a March 2026 supply-chain attack on that action's tags) and `owasp/dependency-check` to an image digest — both need manual re-pinning if/when newer releases should be adopted; neither will auto-update.

Full detail on each of these lives in `.specs/project/STATE.md` under Todos.

## Context

- Branch: `feat/aws-dev-account-config`, clean working tree except ~26 files with unrelated in-progress edits by the user (mostly IDE-driven `using` reordering, plus a real `.WithKafkaUI()` addition to `AppHost.cs`) — confirmed with the user to leave untouched, not part of this session's work.
- Related decisions: AD-012/AD-013 (real AWS dev account, LocalStack narrowed to automated tests only) still hold throughout T09–T17.
