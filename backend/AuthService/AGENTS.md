# AuthService

Owns accounts, roles, profiles, author spaces, user consents, admin user management and audit
log, GitHub account links with org cache, and Telegram links in PostgreSQL schema `auth`
(port 8005). It is the OpenIddict OIDC server for the platform.

## Context routing

Inspect the affected aggregate/use case under `src/`, its endpoint and integration tests. For
login work trace the flow, Identity/OpenIddict configuration, and published `Auth` events together.
Cross-service backend rules come from [`../AGENTS.md`](../AGENTS.md).

## Boundary

Sign-in is email OTP or password; GitHub is link-only and `/auth/github/login` always redirects to
the disabled-login page. Default authentication is OpenIddict JWT validation; browser-navigated
endpoints must opt into the Identity cookie scheme too. Other services read users through
service-role `/internal/` endpoints and `UserCreated`/profile events, never the `auth` schema.

## Entrypoint and verification

Entrypoint: `src/AuthService.Web/Program.cs`.
Run `dotnet test backend/AuthService/tests/AuthService.IntegrationTests`.
