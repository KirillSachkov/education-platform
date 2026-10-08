# Issue tracker

## Authority and cutover

Read [`tracker-authority.json`](tracker-authority.json) before every tracker write or delivery
operation. `active_provider` is the sole authority selector. While it selects `gitlab`,
`activation` must be null. Preparing rules, opening or merging a source MR, a dry run, or creating the
GitHub repositories does not activate GitHub.

Root alone changes the marker to `github` after these external checks pass:

1. Merge and publish the reviewed clean source. Verify actual required GitHub CI and repository
   controls, including public Issues disabled, mandatory PR checks and protected main.
2. Import every currently open platform issue and closed migration children required for hierarchy
   and delivery evidence into the private tracker. Verify mapping, notes, metadata and both ends
   of native relations by read-back. Verify private visibility, Issues enabled and Actions disabled.
3. Verify the initial encrypted historical archive and its restore evidence for issues, MRs,
   comments and uploads. This initial archive/restore receipt is required before activation.
4. Record Q8, the verified import receipt, source/CI/controls receipt and initial archive/restore
   receipt in `activation` fields `owner_command`, `import_receipt`, `checks_receipt`,
   `archive_receipt` and `activated_at` (UTC). Q8 already authorizes this activation;
   do not request approval again.

Activate the authoritative private GitHub tracker before production trials, final GitLab freeze
and retirement. Trials, final archive/restore and consumer checks remain prerequisites for deleting
GitLab VPS/IP; they do not gate tracker activation. GitLab remains available for operational
transition and final archive reads after tracker writes move to GitHub.

The activation commit switches the only writable tracker. Before it, GitHub is an import target
for root only. After it, GitLab issues and historical archives are read-only evidence. Never close GitLab issues
as an import side effect. Migration source acceptance and external acceptance remain separate;
root retains/reopens a source issue closed by its MR until external acceptance passes.

## Native operations before activation

Use authenticated native `glab` for the marker's `gitlab_project`. Let its configured host and
repository remote select the server; do not embed private host addresses in public docs. Use
`glab issue`/`glab mr` for ordinary operations and `glab api projects/1/...` for metadata and
relations. Do not use a transport wrapper or mirror.

Read before and after every write:

```bash
glab issue view <iid> --repo miracle-generation/education-platform
glab api projects/1/issues/<iid>
```

Claim with the current authenticated user's native assignee plus `workflow::in-progress`; verify
both fields. Do not reassign an already claimed issue. Use native links and reciprocal
`Part of #<iid>` / `Blocked by #<iid>` only where native hierarchy is unavailable. Read both ends.
Put the canonical `## Test Plan` in the private issue body or versioned note. Put the delivery
report in the MR. Close only after the authorized merge and required acceptance; remove every
`workflow::*` label at close.

## Native operations after activation

Use native authenticated `gh` for the marker's `github_tracker` and `github_source`:

```bash
gh issue view <number> --repo KirillSachkov/education-platform-internal
gh api repos/KirillSachkov/education-platform-internal/issues/<number>
gh pr create --repo KirillSachkov/education-platform --base main --body-file <public-report>
```

Create/update private issues with `gh issue create|edit`; use `gh api` for metadata and native
relations. Claim with `gh api user`'s login plus `workflow::in-progress`. Verify assignees and labels
before/after writing. Labels preserve the existing project semantics; GitHub organization issue
types do not replace these labels in this personal repository.

Private issues link their public PR. Public PRs contain code behavior, checks and at most a minimal
full-qualified private issue reference. Never copy private issue bodies, notes, attachment paths,
personal data or production configuration into public source, PRs or Actions logs. Root explicitly
closes the matching private issue after the authorized merge and checks. Do not depend on public
cross-repository automatic closure. Remove `workflow::*` labels when closing.

After all mapping entries exist, create native hierarchy and dependencies inside the private
tracker. The URL uses the issue **number**; the JSON uses its numeric global REST **id**:

| Relation | POST endpoint under `repos/<owner>/<repo>` | Body |
| --- | --- | --- |
| Parent contains child | `issues/<parent-number>/sub_issues` | `{"sub_issue_id": <child-id>}` |
| Issue is blocked by blocker | `issues/<blocked-number>/dependencies/blocked_by` | `{"issue_id": <blocker-id>}` |

Read back `sub_issues` and child `parent`; read back `blocked_by` and blocker `blocking`.
Paginate all lists and verify both ends. Preserve archived links to closed blockers not imported;
do not invent replacement issues or use issue numbers as REST ids. See official
[sub-issue API](https://docs.github.com/en/rest/issues/sub-issues) and
[dependency API](https://docs.github.com/en/rest/issues/issue-dependencies).

## Metadata

Types: `type::map`, `type::epic`, `type::task`, `type::bug`, `type::research`, `type::prototype`.
Open workflow: exactly one of `workflow::backlog`, `workflow::ready`, `workflow::blocked`,
`workflow::in-progress`, `workflow::review`.
Executable work: exactly one of `afk` or `hitl`. Wayfinder children add `scope::wayfinder`.
Closed issues have no `workflow::*`; rejected work adds `out-of-scope` before close.

## Shared-skill role mapping

Translate shared skill conceptual roles to project labels in the active tracker:

| Conceptual role | Project label |
| --- | --- |
| `bug` | `type::bug` |
| `enhancement` | `type::task` |
| `needs-triage` | `workflow::backlog` |
| `needs-info` | `workflow::blocked` + `hitl` |
| `ready-for-agent` | `workflow::ready` + `afk` |
| `ready-for-human` | `workflow::ready` + `hitl` |
| `wontfix` | `out-of-scope`; remove `workflow::*`, then close |

Apply the complete mapping and remove conflicting workflow or execution labels.

## Wayfinding operations

- A map is open with `type::map`, `scope::wayfinder`, and one `workflow::*` label.
- A child uses `scope::wayfinder` plus `type::research`, `type::prototype`, or `type::task`.
  Grilling decisions use `type::task`; distinguish them in the title/body.
- AFK research uses `afk`; prototype and grilling use `hitl`; task uses its required mode.
- Use native hierarchy and blocking relations. Before activation only, use the reciprocal text
  fallback when GitLab lacks hierarchy. Always verify both ends after writing.
- The frontier is the map's open, unblocked, unassigned children. Claim with the current
  authenticated user's assignee and `workflow::in-progress` before work.
