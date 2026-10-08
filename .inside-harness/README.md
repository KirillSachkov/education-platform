# Education harness snapshot

This directory is a project-adapted distribution of the Inside `inside-engineering` package.
`product-harness.json` pins the source commit and independently hashes managed skills, local skills,
the generated runtime registry, and Education-owned adaptations. Local operational skills are
`ci-cd-investigate` and `release`.

The upstream `inside-harness health` command expects its managed `WORKFLOW.md` and tracker document
unchanged, so it is not the health command for this adapted repository. Use:

```bash
python3 scripts/ci/validate-agent-instructions.py
bash scripts/ci/test-agent-template-contract.sh
git diff -- .
```

The first two commands validate provenance, digests, discovery links, skill links/frontmatter,
local adaptations, the trunk-based branch contract, instruction budgets, and generated template
builds. The last command shows the current task's harness diff for review.

To update managed skills, change the source package first, merge and pin its full commit, replace
only the declared managed snapshot, preserve Education-owned files, refresh all affected digests
with `python3 scripts/ci/validate-agent-instructions.py --write-digests`,
and run health plus the project contract tests. Never patch a managed skill only here.
