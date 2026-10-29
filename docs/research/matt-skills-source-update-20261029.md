# Matt skill update: source-package decision

Status: installation blocked; reviewed migration options only. No installed skill, discovery
link, workflow, classification state, invocation policy, or provenance identity is changed.
This report is a dated investigation, not a new delivery or publication rule.

## Revisions and source evidence

- Education pre-update commit: `5b6a9e9b8095671a45cb511dcf3815371b9f5ab7`.
- Requested workspace: `5cdf9b286b6b3244fd498e6f12ed58f0684a4ee8`.
- Requested Matt upstream: `a496f5513d4a4cf889bc48e189e579f4b0bdf6aa`.
- Installed package: `inside-engineering@0.3.11`, source
  `sachkov-inside/workspace@3c970cdd2fb89affe7d8aa31197c210294260568`, directory
  `harness/packages/inside-engineering`.
- That package's Matt base: `885e2ca4d842d139e9aef4e48d366c63cb1b8013`, release `v1.2.3`.
- GitHub reports the source repository archived. Its final main is
  `0ace8f08a6295dcb85c3f2ff631c206dfa71411b`; commit
  `125cbde` removed `harness`. The last historical package manifest at `5bea205` is
  `0.4.14` and still records the old Matt base. Updating to that historical version does
  not install the requested revision.
- The archived README points current work to `sachkov-inside/platform`; its root has no
  `harness` directory, and the GitHub contents API for that path returns 404. This is not an
  approved successor package or authority to edit that project.

Sources: [installed package tree](https://github.com/sachkov-inside/workspace/tree/3c970cdd2fb89affe7d8aa31197c210294260568/harness/packages/inside-engineering),
[final source tree](https://github.com/sachkov-inside/workspace/tree/0ace8f08a6295dcb85c3f2ff631c206dfa71411b),
[requested workspace library](https://github.com/KirillSachkov/workspace/tree/5cdf9b286b6b3244fd498e6f12ed58f0684a4ee8/pipeline).

## Installed comparison

Compared every regular file's relative path, permission mode and SHA-256 in all 32 declared
managed directories against the recorded Inside package using `git archive`. All 32 directories
match exactly: there are no unrecorded Education edits inside managed skills.
Education adaptations live in the project contracts and local operational skills.

The requested workspace contains 24 installed names: 18 directories differ and six match.
The remaining eight managed names have no canonical directory in that workspace library and
must retain their recorded sources. No missing skill is selected for installation.

In the following table paths are relative to each skill directory; “absent” means the name is
outside this workspace library, not that the installed skill is missing. Differences include
added, removed and changed files.

| Installed managed skill | Difference from pinned workspace |
| --- | --- |
| `ask-matt` | `SKILL.md` |
| `code-review` | `SKILL.md`, `references/findings.md` |
| `codebase-design` | `DESIGN-IT-TWICE.md` |
| `diagnosing-bugs` | `SKILL.md` |
| `domain-modeling` | `ADR-FORMAT.md`, `CONTEXT-FORMAT.md`, `GLOSSARY-FORMAT.md`, `SKILL.md` |
| `frontend-design` | absent; preserve Inside source |
| `grill-me` | identical |
| `grill-with-docs` | identical |
| `grilling` | `SKILL.md` |
| `handoff` | `SKILL.md` |
| `impeccable` | absent; preserve Inside source |
| `implement` | `SKILL.md`, `agents/openai.yaml` |
| `improve-codebase-architecture` | `SKILL.md` |
| `karpathy-guidelines` | absent; preserve Inside source |
| `modern-web-guidance` | absent; preserve Inside source |
| `playwright-cli` | absent; preserve Inside source |
| `prototype` | identical |
| `research` | `SKILL.md` |
| `resolving-merge-conflicts` | absent; preserve Inside source |
| `setup-matt-pocock-skills` | `SKILL.md`, `classification-setup.md`, `domain.md`, `issue-tracker-github.md`, `issue-tracker-gitlab.md`, `triage-labels.md` |
| `tdd` | `SKILL.md` |
| `teach` | `SKILL.md` |
| `to-questionnaire` | identical |
| `to-spec` | identical |
| `to-tickets` | `SKILL.md` |
| `triage` | `AGENT-BRIEF.md`, `SKILL.md` |
| `vercel-react-best-practices` | absent; preserve Inside source |
| `wait-what` | `SKILL.md` |
| `wayfinder` | `SKILL.md` |
| `web-design-guidelines` | absent; preserve Inside source |
| `wizard` | `template.sh` |
| `writing-for-agents` | identical |

## Requirements that direct copying would lose or change

- `implement` is currently manual, including its Codex adapter. The canonical workspace copy is
  model-invocable and calls absent `implement-spec` and conditionally absent `classify-change`.
  The assignment preserves invocation and forbids adding missing stages or migrating routing.
- Canonical setup would onboard classification and install a five-skill implementation route.
  Education currently has no explicit classification declaration. Preserve that existing state;
  do not run setup or invent a declaration for this update.
- Installed ADR guidance requires status and delegates lifecycle/retention to root `WORKFLOW.md`.
  Canonical ADR guidance makes status optional. Education's required lifecycle must remain
  authoritative after any adoption.
- Education review requires two fresh independent axes, closure by original reviewers, exact-SHA
  evidence, owner QA and explicit merge permission. Optional Security requires an explicit owner
  request or specification requirement. Shared review routing must respect these project facts.
- Keep `.inside-harness/skills/{ci-cd-investigate,release}`, all eight library-absent managed skills,
  `.inside-harness/skills/REGISTRY.md`, discovery links, service instructions, templates, tracker
  authority, workflow, runtime leases, and product/publication gates intact except any mechanically
  required provenance/registry refresh after an approved package update.
- Preserve source-provided Codex metadata and manual invocation policy. An exception for Education
  must be disclosed as an adaptation, never represented as exact canonical content.

## Option A: publish a maintained successor package (recommended contract-preserving path)

The missing decision is the approved writable repository and maintainer for `inside-engineering`,
plus authorization for its source change and release. This assignment owns Education only; the
archived source cannot accept the mandatory source-first merge. Do not unarchive it as a side effect.

After that decision, prepare the package from the recorded `0.3.11` source, with a distinct next
version owned by the successor maintainer. Preserve its 32-skill inventory and all additional-source
metadata. For the 24 shared names, copy eligible directories exactly from the requested workspace
pin. Keep `implement` and `setup-matt-pocock-skills` at their recorded Inside content pending a
separately authorized routing migration; their canonical versions cannot preserve this assignment's
constraints. Keep ADR lifecycle requirements as explicit package/project compatibility guidance;
do not silently weaken the root contract. Verify shared review procedures against Education's
review profile before accepting the package.

The source package's `manifest.json` and `SOURCE.md` must record per-skill source identities,
retained Inside pins, approved adaptations, and the two workspace corrections:
`glab mr list -F json` (issues use `-O json`) and wizard `${e}` Readline marker.
Do not label this mixed distribution a clean Matt upstream pin. Package tests and a source review
must pass, then merge the package and record its full published SHA.

Only then change Education's `.inside-harness/product-harness.json` repository/ref/commit/version,
replace the declared managed snapshot from that package, preserve local and adapted files, refresh
necessary registry entries/digests and run both health checks. Keep a per-skill record of the two
retained older routes as a disclosed limitation of this bounded update. If the owner requires those
routes at the latest version too, that needs a separate routing/invocation compatibility decision.

## Option B: authorize Education to own split-source distribution

Explicitly retire the source-first Inside package constraint for this repository and approve
Education as distribution maintainer. This changes ownership, so it is not implied by installation
authorization. Proposed review scope:

1. Update only the harness ownership paragraphs in root `AGENTS.md` and `.inside-harness/README.md`.
   Leave product, merge, publication and deployment rules unchanged.
2. Evolve `.inside-harness/product-harness.json` and `scripts/ci/validate-agent-instructions.py` to
   validate per-skill repository/commit/path, content digest, and declared adaptations. Preserve the
   historical Inside base and additional sources; do not replace the whole `source` with workspace.
3. Copy eligible canonical directories under `.inside-harness/skills`; retain the two incompatible
   routes and preserve Education requirements as in Option A. Add no library-absent skills.
4. Refresh digests and any necessary registry entries; keep discovery links and local skills intact.
   Run the harness validator and generated-template contract, focused provenance checks, and two-axis
   independent review before preparing the normal delivery request.

This removes dependence on an archived package publisher but requires an owner-approved ownership
migration. Neither option authorizes conversational routing or classification adoption.

## Verification and delivery limits

The unchanged installed harness passes `python3 scripts/ci/validate-agent-instructions.py` and
`bash scripts/ci/test-agent-template-contract.sh` (both generated service solutions build with
zero warnings/errors). Source comparisons above are tied to immutable revisions. The deliverable
is this report; no installed snapshot update has occurred and no new package version is claimed.

No product build/test matrix is selected because product code, templates and harness runtime inputs
are unchanged. Final report checks, independent Standards/Spec review and CI are recorded on the
delivery request. Integration into main awaits the project's explicit owner merge command.
No deployment is in scope. The legacy Education checkout and all other projects remain untouched.
