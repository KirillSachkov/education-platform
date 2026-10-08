---
paths: ["frontend/src/**"]
---

# Frontend architecture

For current browser-platform facts use `modern-web-guidance`; for React/Next performance use
`vercel-react-best-practices`; for a new or materially redesigned interface use `frontend-design`
and the repository's visual lifecycle. Do not invent skill names or assume training data is current.

## FSD direction

`app → pages → widgets → features → entities → shared`

- Imports point only downward. `eslint-plugin-boundaries` is the executable authority.
- Slices at the same layer do not import each other's internals. Move shared domain state down or
  compose both slices from above.
- Consumers import through a slice public API, not a private file path.
- Shared UI/types cannot import entities or features. Cross-feature status primitives used by
  shared components live under `shared`.

## React and application contracts

- Preserve React Compiler assumptions: immutable render inputs and no speculative memoization.
  Add an effect only for synchronization with an external system.
- Use established shared API/auth/query/validation primitives. API paths include a trailing slash.
- Server-only configuration stays server-side; `NEXT_PUBLIC_*` is public build-time data.
- Tailwind and shared shadcn components under `shared/ui/kit` are the default styling primitives.

## Interface verification

Design mobile-first for 320–430 px, keep touch targets at least 44×44 px, and do not require hover.
Inspect changed loading, empty, error, denied/locked, partial, and success states at 393×852 and the
relevant desktop width. Behavioral changes get focused Vitest or the lowest sufficient browser
test; exact classes, copy, decoration, and DOM order are not durable contracts by default.

Run the candidate gate selected by [`testing-profile.md`](testing-profile.md) only after the visual
direction is stable.
