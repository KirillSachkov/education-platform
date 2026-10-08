# AssignmentReviewService

Owns GitHub App VCS installations, AI reviews of student pull requests (iterations, feedback,
student PR messages), project guidelines, issue review specs, and AI model settings in PostgreSQL
schema `assignment_review` (port 8012).

## Context routing

Inspect the affected aggregate/use case under `src/`, its endpoint and tests. For review work
trace the submission event, `AiReview` iteration, GitHub provider, and published verdict
together. Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

ProgressService owns submission status and credit; this service only publishes verdicts
(`AiReviewQueuedForSubmission`, `AiReviewIterationCompleted`). Reviews start from ProgressService
awaiting-review events; review context is copied from EducationContentService events. GitHub
access and LLM calls go through the GitHub App provider and OpenAI-compatible client.

## Entrypoint and verification

Entrypoint: `src/AssignmentReviewService.Web/Program.cs`.
Run `dotnet test backend/AssignmentReviewService/tests/AssignmentReviewService.UnitTests` and
`dotnet test backend/AssignmentReviewService/tests/AssignmentReviewService.IntegrationTests`.
