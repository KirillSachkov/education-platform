---
paths: [".gitlab-ci.yml", ".github/workflows/**", "docker-compose*", "docker/**", "scripts/**", "docs/ops.md", "docs/RUNBOOK.md", "**/Dockerfile*"]
---

# Release pipelines

Read the [tracker authority](issue-tracker.md#authority-and-cutover) first. The active provider
selects task delivery commands. Root's verified operational cutover receipt selects production
commands. Tracker activation precedes production trials and final GitLab freeze/retirement. `main` is the only long-lived branch. A merged source
candidate is not production approval or proof of external GitHub acceptance.

## Delivery path

`short branch from origin/main → reviewed request to main → owner merge → trusted main builds
images → manual owner-authorized deploy`

- Task branches start from current `origin/main`; resolve target drift before merge-ready.
- Before activation, `.gitlab-ci.yml` remains usable: affected-only MR checks include
  `mr-main-gate`; GitLab requires successful pipelines and rejects skipped pipelines. Main builds
  and `prepare-release-images` produce release images. Manual `deploy-production` and
  `rollback-production` remain the authorized legacy release path until operational cutover.
- After activation, GitHub PRs run affected checks and an always-present `required-checks` aggregate.
  Every selected job must succeed; a selected skipped/cancelled/failed job blocks the aggregate.
  Public main protection requires the exact aggregate check from GitHub Actions, strict checks,
  admin enforcement, and forbids force pushes/deletion. Settings and real passing/failing PR
  evidence must be read back before activation.
- PR checks, including forks, use read-only tokens on standard hosted runners. They receive no
  production secrets, package-write permission or access to private legacy images. PRs never
  deploy, roll back, build release images or publish tags.
- Only trusted source-repository main builds all release images into GHCR with immutable full-SHA
  tags and verified manifests. No stale `latest` fallback establishes a release candidate.
- Optional manual integration CI does not replace required local integration evidence in
  [`testing-profile.md`](testing-profile.md). Standard runners and bounded storage are the default;
  paid runners or add-ons require explicit authorization.

## Production deploy after operational cutover

Use the project `release` skill after an explicit owner command for the exact release input.
Discover the reviewed manual production workflow in `.github/workflows`; verify its inputs before
calling `gh workflow run <workflow> --repo KirillSachkov/education-platform --ref main ...`.
The dispatch implementation must independently require the trusted repository and
`refs/heads/main`, plus `environment: production` restricted to branch `main`.

Use independent SSH with a verified host key and private production environment secrets. Release
must not require a GitLab registry, bastion, proxy or private package feed. Legacy pulls use a
dedicated private `read:packages` credential; never grant public repository Actions or forks access
or inherited permissions to private legacy packages. Do not print credentials or private deploy
inputs in public logs. Keep backup, migration, rollback-compatibility, health and metadata guards.

During migration trials, root uses only the approved current/previous private legacy release
inputs. Pending main, revised legal terms and business details must not ship during trials.
Before any future cleaned-source release, verify approved private legal assets, both application
version registries, business runtime data and delivered legal URLs against the protected handoff.
Missing runtime files are not release readiness. Root holds the protected legal-runtime handoff;
public source contains no private assets or production identifiers.

State expected downtime before every production change. Verify images/digests, release metadata,
health and changed user paths afterward. A failed deploy routes to `ci-cd-investigate`; rollback
requires its own matching owner command and the compatibility checks in `docs/RUNBOOK.md`.
Tag only the verified deployed version. Merge and green CI never trigger production deployment.

Do not bypass hooks, force-push main, skip CI, edit production to hide failed jobs, or prune images
before health and rollback state are confirmed. GitHub execution, SSH probe, package privacy and
production trials remain external acceptance until root supplies real read-back evidence.

## Cleanup

After authorized merge or rejection, remove only the task owner's clean worktree/branch after
verifying every commit is preserved remotely. Preserve foreign worktrees and shared Docker state.
