# TrainerService

Owns the interview-prep trainer: tracks, topics, question banks, questions, training and mock
interview sessions, spaced-repetition study state, mastery, bookmarks, AI feedback ratings, AI
usage ledger and stats snapshots in PostgreSQL schema `trainer` (port 8013).

## Context routing

Inspect the affected aggregate under `src/TrainerService.Domain`, its use case under
`Core/Features`, grading code under `Core/Grading` or `Core/Features/Sessions/Grading`, and the
integration tests. Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Paid banks, voice answers and mock interviews require the `TRAINER_PRO` capability; resolve it
only through `TrainerProAccessPolicy` (Redis entitlements) and redact locked content. Open-answer
grading and transcription go through Shared/AI and must respect quotas and the usage ledger.
The service has no Wolverine or RabbitMQ; user lookups call AuthService over HTTP.

## Entrypoint and verification

Entrypoint: `src/TrainerService.Web/Program.cs`.
Run `dotnet test backend/TrainerService/tests/TrainerService.IntegrationTests`.
