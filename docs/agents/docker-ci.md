---
paths: ["**/Dockerfile*", "docker-compose*", ".gitlab-ci.yml", ".github/workflows/**", "nginx*.conf"]
---

# Docker & CI/CD Rules

## Docker
- `NEXT_PUBLIC_*` vars are **build-time ARGs**, not runtime env. Must be passed as `--build-arg` in `docker build`
- Backend healthchecks: `dotnet healthcheck/healthcheck.dll <port>` (not curl)
- Frontend healthcheck: `wget -qO-` (busybox wget on node:22-alpine)
- `docker image prune -f` must run **AFTER** health check succeeds — otherwise rollback images are destroyed on failed deploys

## CI/CD
- `DOCKER_REGISTRY` and `IMAGE_TAG` must be exported in SSH deploy session
- `mkdir -p` for ALL remote directories (including `backups/`) must be the first SSH step
- SSH deploy: never expand secrets on CI runner side — use env-prefix or pre-stored credentials
- After adding a build stage → deploy job must `docker compose pull` before `up -d`
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
