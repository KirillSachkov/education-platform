# Frontend

Next.js 16 App Router application organized with Feature-Sliced Design under `src/`.

## Before changes

- Inspect the changed route, FSD slice, shared primitive, and adjacent tests before editing.
- Frontend path rules in [`../docs/agents/frontend-fsd.md`](../docs/agents/frontend-fsd.md)
  and [`../docs/agents/build-and-env.md`](../docs/agents/build-and-env.md) are authoritative.
- For UI work invoke the routed design/accessibility skills required by those rules before editing.

## Invariants

- FSD imports point downward: `app → widgets → features → entities → shared`; no upward imports.
  `eslint-plugin-boundaries` enforces this in `npm run lint`.
- Reuse shared UI, API, auth, validation, route, and query primitives before adding local variants.
- Server-only values never cross the client boundary; `NEXT_PUBLIC_*` is public build-time data.
- API URLs include a trailing slash. Use the shared Axios client
  (`src/shared/api/axios-instance.ts`) and its envelope errors; show messages via
  `getErrorMessage`.
- Preserve React Compiler constraints (`reactCompiler: true`); do not add memoization or effects
  without a demonstrated need.

## Entrypoints and verification

- Application routes: `src/app/`; shared route constants: `src/shared/config/routes.ts`;
  auth route gating: `src/proxy.ts`.
- Development: `npm run dev` from `frontend/`.
- Verification from `frontend/`: `npm run lint && npm test && npm run build`.
- For UI work, read the visual-iteration and candidate-gate lifecycle in
  [`../docs/agents/testing-profile.md`](../docs/agents/testing-profile.md); full lint, test and
  build belong to the visual candidate, not every provisional edit.
