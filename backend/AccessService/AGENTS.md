# AccessService

Owns plans, plan grants, invite links, T-Bank orders and recurring charges, Trainer Pro offers,
plan onboarding, home pins, Telegram join reminders, and GitHub org invitations in PostgreSQL
schema `access` (port 8010).

## Context routing

Inspect the affected aggregate/use case under `src/`, its endpoint and integration tests. For
billing work trace order, payment webhook, plan grant, and entitlement projection together.
Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Plan grants are authoritative; the Redis `plan:*` entitlement tags are a projection recalculated
from grants on every `PlanGrant*` event, never patched incrementally. T-Bank webhooks are verified
by token before any mutation. GitHub org membership follows grant lifecycle via the GitHub App.

## Entrypoint and verification

Entrypoint: `src/AccessService.Web/Program.cs`.
Run `dotnet test backend/AccessService/tests/AccessService.IntegrationTests`.
