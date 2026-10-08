---
paths: ["**/Dockerfile*", "docker-compose*", ".github/workflows/**", "nginx*.conf"]
---

# Docker & CI/CD Rules

## Docker
- `NEXT_PUBLIC_*` vars are **build-time ARGs**, not runtime env. Must be passed as `--build-arg` in `docker build`
- Backend healthchecks: `dotnet healthcheck/healthcheck.dll <port>` (not curl)
- Frontend healthcheck: `wget -qO-` (busybox wget on node:22-alpine)
- Retain immutable rollback images through deployment and recovery checks.

## CI/CD
- Use the reviewed trusted-main production workflow with pinned SSH host identity.
- Send its verified scripts and private inputs through the fixed stdin transport.
- Verify the exact private backup and pull immutable digests before migrations or startup.
- Use the same audited `RESTORE_POSTGRES_IMAGE` for PostgreSQL and isolated restore drills.
- Migration services share `image:` with their main service (no separate `build:` section)
- `docker-compose.prod.yml` has no `build:` sections — all images come from the selected release registry; after cutover use GHCR

## Nginx
- All location blocks use trailing slashes. Requests without trailing slash get 301 → breaks CORS preflight
- Auth routes (`/connect/`, `/auth/`, `/.well-known/`) have **no `/api/` prefix** — passed as-is to AuthService
- Production nginx has HTTP :80 for internal Docker traffic with `X-Forwarded-Proto: https`

## Security: ForwardedHeaders (#118)

`PlatformOptions.TrustedProxyNetworks` — список CIDR'ов, чьему `X-Forwarded-For` доверяем. По умолчанию `172.16.0.0/12` (Docker bridge networks) + `127.0.0.0/8` (loopback). До этого fix'а `KnownIPNetworks.Clear()` означало «любой источник», и при случайном открытии backend-порта наружу атакующий обходил IP-keyed rate-limit'ы (`/connect/token`, github-webhook, OTP).

**Production checklist:**
- Backend-контейнеры в `docker-compose.prod.yml` биндятся ТОЛЬКО на private network (без `ports:` секции), nginx — единственный наружный listener
- Если меняется prod-топология (host-network nginx, другой proxy chain), переопределить программно через `builder.AddPlatformDefaults("X", o => o.TrustedProxyNetworks = [...])` на конкретном сервисе. Env-override через `__N` суффикс возможен, но удобнее явный код
