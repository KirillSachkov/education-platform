# ADR-001: Versioned database read-contracts

- Status: Accepted
- Date: 2026-07-11

## Context

Services share one PostgreSQL database but own separate schemas. HTTP and integration events remain the default inter-service contracts. CommentService author-feed is a special authorization query: ownership and comment rows must be evaluated in one database snapshot. HTTP introduces a time-of-check/time-of-use window, while an asynchronous projection can be stale after binding changes or a concurrent insert.

## Decision

An owning service may publish a versioned, read-only view or function when an authorization invariant requires a shared snapshot.

- The owner creates it in its own schema through an immutable migration.
- The public name lives in the owner's `.Contracts` assembly.
- Consumers read only the published object, never the owner's private tables.
- Published versions are immutable. Breaking changes require `v2`, consumer rollout, then later removal of `v1`.
- The owner has integration tests comparing the DB contract with its canonical domain/API semantics.
- The contract is read-only and uses the platform database role; no consumer receives write grants to the owner schema.

The first contract is `education.comment_target_ownership_v1`, owned by EducationContentService and consumed by CommentService. It returns `(target_entity_type, target_entity_id, author_id)` for course, material, issue and quiz targets using deterministic binding priority.

## Deployment ordering

The provider migration must complete before any consumer migration or runtime that references a contract version starts. Both development and production Compose definitions therefore make `comment-service-migrations` and `comment-service` depend on `education-service-migrations` with `service_completed_successfully`. A failed or stalled provider migration keeps the consumer stopped instead of exposing a healthy service whose contract-backed endpoint returns 500.

Every new consumer or contract version must add the same owner-first dependency (or an equivalent orchestrator readiness gate) and verify the rendered deployment graph before release.

## Consequences

CommentService can revoke and grant author-feed visibility atomically with ownership/binding mutations and concurrent comments. The services gain a deliberate database-level dependency, but it is versioned, migration-tested and limited to a stable view. Query-plan and scale validation for this view and the author-feed remains tracked in #755.
