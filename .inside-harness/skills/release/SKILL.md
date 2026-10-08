---
name: release
description: Deploy an approved main release to production after an explicit owner command. Check readiness, run the active provider's manual production workflow, and verify production. Also covers urgent main-only production fixes.
---

# Production release

Read [`issue-tracker.md`](../../../docs/agents/issue-tracker.md) for the active authority and
[`release-pipelines.md`](../../../docs/agents/release-pipelines.md) for provider and trial rules.
Migrations follow [`migrations.md`](../../../docs/agents/migrations.md). Readiness is read-only.
Deploy and rollback require the matching explicit owner command.

## Readiness

1. Resolve the exact approved release SHA and successful trusted-main image build. Record immutable
   tags/manifests and commits since the deployed release using `docs/ops.md` read-only procedures.
   During migration trials, use only root's approved current/previous private legacy inputs;
   pending main and revised legal terms never ship. Source merge is not operational cutover.
2. Inspect migrations, configuration/secret names, dormant service activation and backfills.
   Deleted/renamed committed migrations block release. For cleaned source, ask root for its protected
   legal-runtime handoff; verify private assets, both version registries, business runtime data
   and legal URLs. Missing inputs block readiness.
3. Verify baseline, backup and rollback compatibility using `docs/ops.md` and `docs/RUNBOOK.md`.
   A degraded baseline is No-Go and routes to `ci-cd-investigate`. Confirm actual operational
   cutover evidence before choosing GitHub; source checks do not prove SSH or production readiness.

Report Go/No-Go, exact release input, migrations, configuration, owner actions, baseline and backup.

## Deploy

4. Match the owner command to the exact release input. Land any release-version source change
   through the active provider's ordinary main request first. State expected downtime.
5. Before operational cutover, play the candidate's manual GitLab job with native
   `glab api -X POST projects/1/jobs/<deploy-job-id>/play`.
   After cutover, inspect the reviewed production workflow and its input schema, then use native
   `gh workflow run <workflow> --repo KirillSachkov/education-platform --ref main ...`.
   Verify trusted main, production environment, independent SSH/host-key checks and private
   credential boundaries. Never grant public Actions access to private legacy packages.
6. Monitor the exact run to completion. GitHub uses `gh run view <run-id> --repo
   KirillSachkov/education-platform` and `gh run watch <run-id> --repo
   KirillSachkov/education-platform --exit-status`; GitLab uses the candidate pipeline/jobs API.
   Failure stops the deploy and routes to `ci-cd-investigate`. Rollback needs a matching command.
7. Verify images/digests, deployed SHA/version, migrations, health, changed user paths and bounded
   logs using `docs/RUNBOOK.md`. Verify the expected release tag only after successful deployment.

## Urgent fixes

Use a minimal short branch from `origin/main`, local checks, two-axis review, approved merge and
these deploy steps. Schema, messaging and Shared changes retain required integration evidence.
Never skip CI, bypass hooks, force-push main, hand-create CI-owned tags, or mutate production
outside the audited workflow. Report actual deployed input, checks and remaining risk in the
active private tracker. Root closes shipped issues only after authorized matching merge/checks.
