---
paths: ["frontend/**", "backend/**", "scripts/ci/**", ".github/workflows/**"]
---

# Quality gates

Select verification by changed risk using [`testing-profile.md`](testing-profile.md). The scripts
and configuration named below are executable authorities; do not copy their current debt counts or
job inventory into prose.

- Frontend candidate: `npm run lint`, `npm test` and `npm run build` from `frontend/`; CI adds the
  ESLint and TypeScript-strict ratchets and diff-scoped Prettier.
- Backend candidate: focused tests during implementation, then the affected build and integration
  suites. `TreatWarningsAsErrors` and code-style build settings make warnings blocking.
- Contract changes: run the matching `scripts/ci/test-*.sh` contract test before its validator.
- Architecture: `scripts/ci/validate-arch.sh` enforces cross-service and layer boundaries.
- Ratchets never grow casually. When verified debt falls, update the relevant baseline in the same
  MR; a new suppression needs a local reason.

Every delivery request to `main` runs its affected executable checks. Only the push to `main` builds images, and
production deploy is a separate manual job. See [`release-pipelines.md`](release-pipelines.md).

Every reported pass is bound to the candidate revision by a fresh run or the complete reuse bundle
defined in `testing-profile.md`. An intentionally absent/skipped job is not a successful gate.
