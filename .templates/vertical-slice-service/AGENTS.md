# ServiceName

TODO: replace this line with the service's owned business capability. PostgreSQL schema
`__SERVICE_SCHEMA__`; HTTP port `__SERVICE_PORT__`.

## Boundary

This small service keeps domain, vertical slices, persistence, and web startup in one project while
retaining a separate Contracts project. Other services depend only on Contracts or integration
events and never query this schema.

## Entrypoint and verification

Entrypoint: `src/ServiceName/Program.cs`.
Run `dotnet test backend/ServiceName/tests/ServiceName.IntegrationTests`, then the backend solution
build when a public or shared contract changes. Cross-service rules come from `../AGENTS.md`.
