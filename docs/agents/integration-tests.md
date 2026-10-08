---
paths: ["**/IntegrationTests/**", "**/tests/**"]
---

# Integration tests

- Follow the affected suite's `WebApplicationFactory`, Testcontainers, Respawn, authentication, and
  fake-entitlement conventions. The factory code is authoritative; services do not share one exact
  infrastructure matrix.
- Disable external Wolverine transports unless the test explicitly owns a broker. Use the outbox
  pattern documented in [`wolverine-tests.md`](wolverine-tests.md).
- Authenticate through suite helpers and assert denial paths as well as success. Test persistence,
  authorization, constraints, idempotency, and observable contracts at the lowest sufficient seam.
- Production IDs use version 7 GUIDs; arbitrary IDs are acceptable test inputs unless identifier
  ordering is itself under test.
- Run Testcontainers suites on the clean candidate as selected in
  [`testing-profile.md`](testing-profile.md); they need a Docker daemon, not the shared Compose
  runtime. Compose-based checks still require the runtime lease.
- Cross-schema setup must not assume another service database exists. Create only the explicit test
  fixture contract needed by the suite.
