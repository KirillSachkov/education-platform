# Shared AI

Provider-agnostic AI library. `AI` defines `IAiClient`, embeddings, transcription, token
estimation, the named-provider `IAiClientFactory`, `MeteredAiClient` metrics, and reusable
skills (classifier, structured extractor, summarizer). `AI.OpenAiCompatible` adapts any
OpenAI-compatible endpoint, configured under the `AI` section.

## Context routing

Consumers: AssignmentReviewService and ProgressService. A contract change affects all of them; inspect their registrations before
editing. Cross-service backend rules come from [`../../AGENTS.md`](../../AGENTS.md).

## Boundary

Service core code depends only on the `AI` contracts; only Web or infrastructure registration
references `AI.OpenAiCompatible`. Failures return `Result<T, Error>` rather than exceptions.
Structured output is validated against its JSON schema before reaching callers.

## Verification

Run `dotnet test backend/Shared/AI/AI.OpenAiCompatible.Tests`, then build and test the affected
consumer services.
