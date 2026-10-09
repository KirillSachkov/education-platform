# ProgressService

Owns learner progress: enrollments, material views, module/project/issue progress, issue
submissions and review workflow, quiz attempts and bookmarks in PostgreSQL schema `progress` (port 8003).

## Context routing

Inspect the affected aggregate under `src/ProgressService.Domain`, its use case or event handler
under `Core/Features`, the endpoint and integration tests. Submission state changes cascade
through domain events into issue, module and project progress; trace them together.
Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Content access is checked through `IEntitlementChecker` (Redis tags written by AccessService)
before progress is recorded; never infer access from client payload. Submission events for
NotificationService leave through the outbox.

## Entrypoint and verification

Entrypoint: `src/ProgressService.Web/Program.cs`.
Run `dotnet test backend/ProgressService/tests/ProgressService.IntegrationTests`.
