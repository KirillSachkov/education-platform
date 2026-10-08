# Agent instruction architecture

This repository uses progressive disclosure: a small always-loaded instruction chain routes an
agent to reference and workflows only when the task needs them.

## Information hierarchy

| Layer | Contains | Must not contain |
|---|---|---|
| Root `AGENTS.md` | Repository identity, authority boundaries, command-time safety, scope routing | Service catalogs, histories, long command lists |
| Scoped `AGENTS.md` | Scope boundary, critical invariants, conditional pointers, focused verification | Complete domain models, endpoint tables, implementation diaries |
| `docs/agents/*.md` | Behavior selected mechanically by file path | Domain encyclopedia or historical rationale |
| Skills | Multi-step process with completion gates | Stable project facts already in docs |
| General `docs/` | Operational and domain sources of truth | Duplicate agent-only summaries |

## Budgets

- Root `AGENTS.md`: at most 6,144 bytes.
- Concatenated project instruction chain from root to any nested `AGENTS.md`: at most 32,768 bytes.
- Every `CLAUDE.md`: at most 1,024 bytes and exactly one local `@AGENTS.md` import.
- A budget is a ceiling, not a target. Remove no-op, stale, duplicated, and environment-discoverable
  prose before adding another pointer.

These limits intentionally fit Codex's default project-document budget. Do not raise the runtime
limit to conceal instruction growth.

## Context pointers

A pointer names both the target and the branch that triggers it. Prefer:

> For messaging topology changes, read `MESSAGING_CONVENTIONS.md`.

Avoid an unqualified list of "useful docs". Agents should open a small scope index first and then
only the reference sections whose trigger matches the task.

## Canonical homes

- Messaging topology and its maintenance contract:
  `backend/Shared/Messaging/RabbitMqMessaging/MESSAGING_CONVENTIONS.md`.
- Tracker behavior: `docs/agents/issue-tracker.md` and delivery skills.
- Test/review facts: `docs/agents/testing-profile.md` and `docs/agents/review-profile.md`.
- Shared Docker lease lifecycle: `docs/agents/local-runtime.md`; command-time guard remains in root.
- Migration immutability: `docs/agents/migrations.md` and the blocking CI check.
- Product/service detail: code, tests, and the nearest scoped `AGENTS.md`; avoid generated prose
  mirrors of current implementation.

## Maintenance

When behavior changes:

1. Update the existing canonical reference section that owns the fact.
2. Change a scoped `AGENTS.md` only when its boundary, critical invariant, pointer, entrypoint, or
   verification command changes.
3. Change root only when guidance is relevant before every project task or before command routing.
4. Add a path-scoped rule only when file selection can deterministically trigger the behavior.
5. Add a skill only for a reusable multi-step workflow with a completion gate.
6. Do not copy the same fact into root, service instructions, and messaging docs.

Run before submitting instruction changes:

```bash
python3 scripts/ci/validate-agent-instructions.py
bash scripts/ci/test-agent-template-contract.sh
```

The validator checks discovery links, instruction budgets, local pointers, thin Claude bridges,
managed/local/adapted provenance, the main-only branch contract, templates, and forbidden legacy
layers.
The template contract then generates both service variants and builds their solutions.
