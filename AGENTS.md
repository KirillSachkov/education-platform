# Education Platform

.NET 10 microservices backend and Next.js 16 frontend for SachkovLearn. PostgreSQL is
schema-per-service; RabbitMQ/Wolverine carries integration events; Redis stores cache and
entitlement projections.

## Delivery contract

- Read [`WORKFLOW.md`](WORKFLOW.md) for issues, branches, worktrees, merge readiness and owner
  gates. Skill instructions define their own invocation and completion steps.
- Before tracker or delivery operations, read
  [`docs/agents/issue-tracker.md`](docs/agents/issue-tracker.md). Its authority marker selects exactly
  one writable tracker; preparing or merging migration source never activates another tracker.
- `main` is the only long-lived branch. Work goes through short branches and reviewed requests to `main`.
  Merging, deploying to production, and changing production require an explicit owner command.
- Preserve foreign dirty changes and worktrees. Never repurpose another live session's worktree.
- `Pipelines must succeed` is mandatory, but green CI does not replace task-selected local evidence.

## Command-time safety

- Docker Compose runtime is shared across worktrees. Before `up*`, `down`, rebuild, migration, or
  another state-changing Docker command, acquire or receive the canonical
  `$(git rev-parse --git-common-dir)/local-runtime.lease`. Read
  [`docs/agents/local-runtime.md`](docs/agents/local-runtime.md).
- Use native `glab` for GitLab and `gh` for GitHub; tracker writes follow the authority marker.
  Project MCP servers are declared in `.mcp.json`; inspect only the needed server's schema before
  calling it.
- Never commit secrets. Production diagnostics are read-only unless an operational skill and an
  explicit owner command authorize mutation.

## Scope routing

- Backend: read [`backend/AGENTS.md`](backend/AGENTS.md), then the nearest service `AGENTS.md` and
  inspect the affected code and tests.
- Frontend: read [`frontend/AGENTS.md`](frontend/AGENTS.md).
- Messaging topology: read
  [`MESSAGING_CONVENTIONS.md`](backend/Shared/Messaging/RabbitMqMessaging/MESSAGING_CONVENTIONS.md).
- Infrastructure and recovery: read [`docs/ops.md`](docs/ops.md) and
  [`docs/RUNBOOK.md`](docs/RUNBOOK.md).
- Internal admin tools: read [`mcp/admin-server/README.md`](mcp/admin-server/README.md).
- Testing and review: read [`docs/agents/testing-profile.md`](docs/agents/testing-profile.md) and
  [`docs/agents/review-profile.md`](docs/agents/review-profile.md) when the selected skill routes
  there.

## Repository invariants

- Committed migrations are immutable; create a corrective migration instead. Read
  [`docs/agents/migrations.md`](docs/agents/migrations.md).
- Production identifiers use `Guid.CreateVersion7()`.
- Business failures use the project Result/Error pattern, not exceptions.
- API URLs include a trailing slash because nginx redirects break CORS preflight.
- Frontend dependencies follow FSD direction; no upward imports.
- Detailed behavior belongs behind conditional pointers in docs, scoped references, or skills.
  Follow [`docs/agents/instruction-architecture.md`](docs/agents/instruction-architecture.md).

## Agent harness

- `.inside-harness/skills` is the single skill snapshot: versioned Inside skills plus
  project-owned operational skills. Provenance and ownership live in
  [`.inside-harness/product-harness.json`](.inside-harness/product-harness.json).
- `.agents/skills` and `.claude/skills` are discovery links to that snapshot. Runtimes without
  native discovery use [`.inside-harness/skills/REGISTRY.md`](.inside-harness/skills/REGISTRY.md).
- Do not edit managed Inside skills in this repository. Update the source package, version, and
  snapshot together. Project-owned skills may be changed here without shadowing managed names.
- `WORKFLOW.md`, project rules under `docs/agents`, and local operational skills are Education
  Platform adaptations; an upstream harness update must preserve them.
- `.harness/skills` and `.harness/rules` are compatibility links only. Add no locks, overlays,
  gates, migrations, skills, or rules beneath `.harness`.
- Invoke a skill only when its description matches the task. There are no hidden command gates.

## Minimal verification

- Backend: `dotnet build backend/backend.slnx` plus affected service tests.
- Frontend: `cd frontend && npm run lint && npm test && npm run build`.
- Harness structure, provenance, and instruction hierarchy:
  `python3 scripts/ci/validate-agent-instructions.py`.
- Generated service templates: `bash scripts/ci/test-agent-template-contract.sh`.
