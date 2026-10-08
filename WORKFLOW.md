# Delivery workflow

This is the Education Platform engineering agreement. Skills own task-specific procedures; this
file owns the shared tracker, branch, worktree, readiness, and owner-gate rules.

## Tracker and issue contract

Read [`docs/agents/issue-tracker.md`](docs/agents/issue-tracker.md) and its authority marker first.
Until root records verified import and explicit activation, only GitLab is writable. After activation,
only the private GitHub tracker is writable; public Issues remain disabled. Source preparation and
its merge can precede external acceptance. They never activate the tracker.
Product work, bugs, architecture changes, and substantial documentation changes start from one
repository issue. A trivial chore may go directly to a small merge request when it needs no
discussion, dependency tracking, or owner decision.

An executable issue states the observable outcome, why it matters now, scope and exclusions,
acceptance criteria, dependencies, verification, unresolved owner decisions, and one stopping
condition. Use native tracker relations when available; otherwise record one unambiguous relation
in both affected issues and read them back after mutation.

## Branches, worktrees, and merge requests

`main` is the only long-lived branch and represents the production release line. Every task
branch starts from current `origin/main`, stays short, and returns through an MR before activation
or public PR after activation, always to `main`.

Tracked work uses `<type>/<issue>-<slug>`. Before activation, its GitLab MR includes `Closes #<issue>`.
After activation, its public PR links the full private issue reference without copying private
business text. Root explicitly closes the private issue after the authorized matching merge and
checks; public cross-repository closing keywords are not the closure contract.
Untracked chores use `<type>/<slug>`. Supported types are `feat`, `fix`, `docs`, `chore`,
`research`, and `prototype`. One meaningful task has one branch, one writing worktree, and one
delivery request.
Every `glab mr create` supplies `--target-branch main` explicitly; the GitLab default branch is
mutable project state and never selects a delivery path. After activation, every `gh pr create` supplies
`--repo KirillSachkov/education-platform --base main` explicitly.

The primary checkout is owner-controlled. Inspect it read-only unless the owner explicitly asks to
change it. Create a task worktree without switching or modifying that checkout. Treat foreign
worktrees, branches, and dirty changes as live state. Supporting agents are read-only; independently
mergeable child tasks get separate branches and worktrees.

Each delivery request states the result, verification performed, anything not tested, open decisions,
and UI evidence when the interface changed. Never force-push `main`, bypass hooks, or hide a
missing check. After an owner-authorized merge or issue closure, the task owner removes only its
clean worktree and branch after verifying every commit is preserved remotely.

## Release path

The normal path is:

`task branch → reviewed MR/PR to main → owner merge → trusted main builds images → manual owner-run deploy`

Delivery requests and deploys follow [`docs/agents/release-pipelines.md`](docs/agents/release-pipelines.md).
Production deploy runs only through the project `release` skill after an explicit owner command.
A green request or a merged `main` never implies permission to deploy. Production incidents route to
`ci-cd-investigate`; an urgent fix uses the same short-branch path with a minimal diff.

## Ready and done

Work is ready to implement when its outcome, scope, acceptance criteria, dependencies, verification,
and owner decisions are known. Multi-session work also needs an agreed decomposition and task
relations.

Work is ready for owner merge when:

- acceptance criteria are met without silently expanding scope;
- focused checks and the task-selected repository gates pass on the exact candidate;
- required Standards and Spec review findings are fixed, explicitly deferred to a linked issue, or
  rejected with evidence, and affected checks are rerun after fixes;
- durable docs and ADRs reflect confirmed decisions;
- the delivery request links its issue when applicable and reports verification and omissions;
- user-interface changes include relevant browser evidence;
- no unresolved safety, data, security, rollout, or product decision remains.

`Pipelines must succeed` is mandatory where a pipeline exists. It never substitutes for local
evidence required by [`docs/agents/testing-profile.md`](docs/agents/testing-profile.md). Merge
readiness is not merge permission.

## Review closure

Review implementation, specification, and architecture changes from one fixed baseline along two
axes: repository Standards and the originating Spec. Every finding is either fixed, rejected with
evidence, or deferred to a linked private tracker issue with owner agreement. After a fix, rerun
affected
checks and repeat review from the same baseline. A green pipeline does not close an unresolved
finding.

## Architecture fitness

At completion, check whether the changed seam became easier to understand, test, and change. Prefer
deep modules, explicit domain language, and one direction of dependency. Record only hard-to-reverse
trade-offs as ADRs; ordinary implementation detail belongs in code and tests. Do not expand task
scope merely to pursue an aesthetic refactor.

## Pruning

Remove duplicate, stale, speculative, or superseded instructions and code discovered in the task's
scope. Git history is the archive. ADRs are the exception: they use `proposed`, `accepted`,
`deprecated`, or `superseded` status; a superseded ADR links the newer ADR and remains in place.
Delete no still-authoritative evidence merely to make the tree smaller.

## Durable knowledge

Keep one authority for each rule. Prefer an executable type, schema, test, lint, or guardrail over
duplicated prose. Put stable project facts in scoped docs, reusable procedures in skills,
hard-to-reverse decisions in ADRs, and deferred work in the active private tracker.

Every completed task hands off the outcome, material caveats, verification, and direct links to its
issue, delivery request, or durable document. A local file path alone is not a delivery handoff.

## Owner gates

Explicit owner approval is required for:

- every merge into `main`;
- deployments, rollbacks, and production mutations;
- payments, credentials, secrets, external messages, publishing, and other risky external writes;
- product or visual decisions and hard-to-reverse ADR trade-offs.

Within those boundaries an agent may implement a ready issue autonomously, gather evidence, push a
task branch, and prepare a delivery request. Public reports describe code behavior and checks;
private task bodies, imported notes, archives and credentials stay outside public source. Only the
owner, or an agent acting after the corresponding explicit owner command, may cross a gate.
