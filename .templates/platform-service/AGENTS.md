# ServiceName

TODO: replace this line with the service's owned business capability. PostgreSQL schema
`__SERVICE_SCHEMA__`; HTTP port `__SERVICE_PORT__`.

## Boundary

This service owns its domain and schema. Other services use Contracts HTTP clients or integration
events; never reference another service's Domain/Core/Infrastructure project or schema.

## Entrypoint and verification

Entrypoint: `src/ServiceName.Web/Program.cs`.
Run `dotnet test backend/ServiceName/tests/ServiceName.IntegrationTests`, then the backend solution
build when a public or shared contract changes. Cross-service rules come from `../AGENTS.md`.
