# EducationContentService

Owns courses, modules, materials, projects and issues with review specs, quizzes, collections, roadmaps, and short links in PostgreSQL schema `education`
(port 8001).

## Context routing

Inspect the affected aggregate/use case under `src/`, its endpoint and integration tests. For
material placement, access, or media rules read `MATERIAL_LIFECYCLE.md` first.
Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

ECS is authoritative for content `AccessType` and writes per-resource Redis access tags
(`access:public`, `authenticated`, `plan:all`, `plan:course:{id}`); AccessService owns user plan
tags. It publishes `Education` lifecycle events and serves `/internal/` lookups for Progress,
Search, and ownership, including the versioned view `education.comment_target_ownership_v1`.
It confirms FileService media bindings with `FileAssetBindingConfirmed`.

## Entrypoint and verification

Entrypoint: `src/EducationContentService.Web/Program.cs`.
Run `dotnet test backend/EducationContentService/tests/EducationContentService.IntegrationTests`.
