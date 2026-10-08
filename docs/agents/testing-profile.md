# Testing profile

Live Education Platform facts for agents planning and verifying project work. This file
does not prescribe a second lifecycle: select the lowest layer that can fail for each changed risk,
then bind evidence to the exact candidate SHA.

## Fast layers

- Pure/domain behavior: targeted project test with `dotnet test <project> --filter ...`.
- Shared backend primitives: their dedicated `backend/Shared/*/*.Tests` project plus affected
  service tests.
- Frontend branching logic, state and mutations: focused Vitest at the lowest sufficient seam.
- Contract/policy scripts: their `scripts/ci/test-*.sh` test first, then the contract command.
- Formatting: `dotnet format backend/backend.slnx --verify-no-changes` for touched backend scope;
  frontend formatting is enforced by its own hooks/checks.

## Frontend visual lifecycle

Separate design exploration from behavior verification and the final candidate gate.

### Provisional visual loop

For spacing, color, borders, decoration, exact copy, composition, non-semantic artwork and block
order while the direction is changing:

1. Run `cd frontend && npm run lint:fast -- <changed-files>` or another scoped static check for
   the edited files. Full-project typecheck belongs to the candidate gate.
2. Inspect the affected loading, empty, error, locked, partial and success states in the real
   browser at the relevant mobile and desktop widths.
3. Record screenshots or concise browser observations for the next visual decision.

Do not create persistent CSS-source, exact-class, snapshot or DOM-order assertions for these
provisional choices. If the same edit changes branching, interaction, navigation, accessibility
semantics, data fetching, auth/access or mutations, cover that behavioral seam immediately with
focused Vitest or the lowest sufficient browser check.

### Visual candidate gate

The agent may declare a visual candidate when the requested states and widths were inspected, no
known visual finding remains, and further expected work is polish rather than a direction change.
Record this basis in the issue/MR evidence. An explicit owner gate in the ticket/spec overrides
agent declaration.

Run once on that exact clean candidate SHA:

```bash
cd frontend && npm run lint && npm test && npm run build
```

Record the SHA and results in the issue/MR evidence.

Add Playwright only for changed risks that exist in the assembled application, using the relevant
states and widths. Any material post-gate change invalidates the evidence and creates a new
candidate. Full role/route, cross-browser and release-wide matrices are pre-release/on-demand, not
part of ordinary visual iteration.

Persistent visual automation remains selective after candidate declaration: add it only for an
explicit stable observable contract with demonstrated regression cost. Exact CSS values, copy,
class names, decorative presence and block order are not contracts by default.

## Integration and cross-cutting layers

Run integration suites only after the task has a clean candidate; during implementation run the
focused command at the lowest seam instead. Suites use Testcontainers and need a running Docker
daemon, not the shared Compose runtime.

```bash
dotnet test backend/<Service>/tests/<Service>.IntegrationTests
dotnet test backend/backend.slnx          # cross-cutting candidate
```

- Endpoint persistence/authorization, messaging, DB constraints and service wiring require the
  affected service's integration suite; `Shared/**` or cross-service changes require every suite.
- User-visible browser flows need a real-browser check of the relevant states and widths.
- Compose-based checks require ownership of the shared runtime lease
  ([`local-runtime.md`](local-runtime.md)). A missing or foreign lease means `verification
  pending`, never a silent skip.

### Evidence invalidation

A material change invalidates only the evidence whose executable or input scope changed:

- product/runtime, migration, Compose or test behavior changed → rerun the affected check on the
  new clean SHA;
- only documentation or a report changed while the covered runtime scope is byte-identical →
  bind the earlier result to the final SHA with `git diff --quiet <evidence-sha>..HEAD --
  <covered paths>` and name the covered paths in the MR report;
- a failed diagnostic remains `FAILED`; a later focused pass does not relabel it green.

## CI mapping

Read the [authority marker](issue-tracker.md#authority-and-cutover) first. After activation,
GitHub runs affected PR checks plus the mandatory `required-checks` aggregate; inspect the actual
`.github/workflows` and selector for job selection. Selected failed/cancelled/skipped jobs block
the aggregate. Optional manual integration never replaces required local evidence. Real GitHub
execution remains external acceptance until verified by root.

Before activation, MRs to `main` run affected-only jobs selected by `changes:` in `.gitlab-ci.yml`:

- `mr-main-gate`: always present; whitespace check against the target branch.
- `unit-tests`, `build-frontend`: backend unit tests and frontend lint, tests and build.
- `check-migrations`, `validate-compose-contracts`, `validate-test-rules`: migration, Compose,
  test-rule and architecture contracts.
- `validate-agent-skills`: harness and instruction integrity.
- `integration-tests:all`: optional manual full matrix. It never replaces the local integration
  evidence required above.

A green pipeline does not replace task-selected local evidence. Reachable business branches,
negative paths, boundary values, auth/access denial, idempotency/concurrency and production wiring
are selected by risk; line coverage alone is not the goal.
