# Review profile

Project facts for the clean project `code-review` workflow. Review the exact delivery source SHA after every
required Test Plan check has passed; an older verdict never certifies a later fix commit.

## Required axes

1. Standards/project rules: root and module `AGENTS.md`, applicable `docs/agents/`, module
   boundaries, migrations, Wolverine, transactions, FSD imports, trailing-slash API URLs and
   existing code conventions.
2. Spec fidelity: issue/Epic body, accepted decisions, observable acceptance criteria and the
   actual diff. Flag missing scope, scope creep and behavior that diverges from the current spec.

Run the axes in two independent fresh contexts. Keep reports separate. Each finding needs
file/line evidence, impact and a fix direction; tooling-enforced style is not a review finding.
Adjacent best-practice work is an observation unless the ticket includes it.

Security is an optional third reviewer only when the owner explicitly requests it or the
issue/specification requires it. Do not infer that requirement merely from the subsystem or files
touched. Every reviewer that raised a blocker independently confirms its closure on the new SHA
after the author fixes it and the selected verification is rerun.

The delivery request description is the canonical review/report surface. Merge-ready requires both default axes
green, any explicitly required Security axis green, all findings closed by their original reviewer,
Test Plan evidence bound to the latest source SHA, green required CI, and no conflicts. Evidence is
latest-SHA-bound either by a fresh result on that SHA or by the explicit reuse bundle from
`testing-profile.md`: prior passing summary, a final-SHA `git diff --quiet` over the complete
positive covered-input path set, and final-SHA direct checks for every changed test/report/doc. A
reviewer must reject an incomplete path set or a changed covered input. Standard execution stops
for owner QA and an explicit merge command.
