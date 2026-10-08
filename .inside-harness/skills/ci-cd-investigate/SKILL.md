---
name: ci-cd-investigate
description: Use when a CI run failed or is stuck, a production deploy failed, or production is unhealthy. Gather evidence first, then choose retry, fix-forward, or owner-authorized rollback.
---

# CI/CD investigation

Read [`issue-tracker.md`](../../../docs/agents/issue-tracker.md) for tracker/report authority and
[`release-pipelines.md`](../../../docs/agents/release-pipelines.md) before interpreting job presence.
PR/MR checks never deploy; only trusted main builds release images. Deploy/rollback are manual.
Diagnose the actual run provider, including prepared GitHub CI before tracker activation.
Tracker activation precedes production trials and final GitLab freeze/retirement.
Source preparation does not establish operational cutover.

## Evidence loop

1. Resolve the exact SHA, run and first failed job. For a GitLab run use native `glab api
   projects/1/pipelines/<id>`, `glab api 'projects/1/pipelines/<id>/jobs?include_retried=false&per_page=100'`
   and `glab api projects/1/jobs/<job-id>/trace`. For a GitHub run use native
   `gh run view <id> --repo KirillSachkov/education-platform --json headSha,status,conclusion,jobs`
   and `gh run view <id> --repo KirillSachkov/education-platform --log-failed`.
   Keep private credentials, production configuration and task bodies out of public reports/logs.
2. Classify from the trace: retry a transient network/registry/runner failure once; fix forward for
   repeatable compile/test/migration/configuration failures. For production health failures, inspect
   bounded read-only evidence using [`ops.md`](../../../docs/ops.md) and
   [`RUNBOOK.md`](../../../docs/RUNBOOK.md).
3. Re-read replacement job/run and verify the affected environment. Two identical failures are
   reproducible until evidence proves otherwise. Selected skipped/cancelled jobs are not passes.

Retry only failed jobs when possible. For a GitLab run use `glab api -X POST
projects/1/jobs/<job-id>/retry`; for a GitHub run use `gh run rerun <run-id> --failed --repo
KirillSachkov/education-platform`. A production workflow retry is a production mutation and needs
explicit matching owner authorization; routine CI retries do not authorize deployment.

For a fix, use a short branch from current `origin/main` in its own writing worktree, local checks
and a main request. Supply `--target-branch main` to `glab mr create` before activation or
`--repo KirillSachkov/education-platform --base main` to `gh pr create` afterward. Retain relevant
failure evidence privately and summarize code behavior/checks publicly. Do not edit unrelated
files merely to trigger CI.

## Production boundary

Read-only public checks and bounded diagnostics are allowed. Deploy, restart, secret/config
mutation, rollback, pipeline cancellation and infrastructure cleanup require the matching
operational skill and explicit owner command. Use the reviewed manual workflow selected by actual
operational cutover, with the compatibility guards in `docs/RUNBOOK.md`. Do not reproduce it with
ad-hoc SSH commands or restore GitLab transport dependencies after cutover.

Report root cause, evidence, action, exact current run/deploy state and remaining risk in the
active private issue or safe delivery report. Distinguish prepared source from external acceptance.
