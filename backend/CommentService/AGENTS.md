# CommentService

Owns threaded comments on education entities (ltree paths, cursor pagination), the comment inbox,
and the per-author feed read state in PostgreSQL schema `comments` (port 8004).

## Context routing

Inspect the affected use case under `src/CommentService.Core/Features`, its endpoint and
integration tests. For access work trace the Redis entitlement check and EducationContentService
ownership lookup together. Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Creating a comment requires Redis content access plus course enrollment or target management;
ownership comes from EducationContentService, and replies inherit the parent target.
`target_author_id` is only a read-model snapshot: the author feed authorizes against the
ECS-owned view `education.comment_target_ownership_v1` in the same query. Publishes
`CommentCreated`; deletes comments on ECS `*HardDeleted` events.

## Entrypoint and verification

Entrypoint: `src/CommentService.Web/Program.cs`.
Run `dotnet test backend/CommentService/tests/CommentService.IntegrationTests`.
