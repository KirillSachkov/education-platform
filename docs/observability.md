# Observability

Стек, шина данных, кастомные метрики и реализация. В root `CLAUDE.md` остаётся только
один абзац c командой `./scripts/dev.sh up-obs` и debug order — подробности здесь.

## Стек

**Grafana suite (OSS)**: Loki (логи), Tempo (трейсы), Prometheus (метрики), Grafana (UI).
OTel SDK в .NET-сервисах для traces+metrics, Alloy для shipping логов из Docker stdout.

## Шина данных

| Сигнал | Источник | Канал | Хранилище |
|---|---|---|---|
| Логи | Serilog Console → stdout контейнера | Alloy (читает Docker socket) | Loki |
| Трейсы | OTel SDK (`AddOtlpExporter`) | OTLP gRPC → otel-collector | Tempo (с tail-sampling в prod: errors+slow+10%) |
| Метрики | OTel SDK + exporters (postgres/redis/rabbitmq) | OTLP → otel-collector → `:8889`, exporters scrape напрямую | Prometheus |

## Режимы

**Dev (default):** OTEL push **выключен** (`OTEL_EXPORTER_OTLP_ENDPOINT` пустой, `.AddOtlpExporter()` не регистрируется) — `up`, `up-fe`, `up-all` не поднимают obs. Логи только в `docker compose logs <service>`.

**Dev (с obs):** `./scripts/dev.sh up-obs` — единственная команда, поднимающая Grafana stack и экспортящая `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`. Grafana на `http://localhost:3001`.

**Prod:** Grafana via SSH tunnel (`ssh -L 3001:localhost:3001 root@server`). `OTEL_EXPORTER_OTLP_ENDPOINT` задан в Infisical.

**Retention:** Loki 14d (prod) / 72h (dev), Tempo 7d, Prometheus 15d (prod) / 7d (dev).

## Bootstrap unification

Все 9 backend сервисов используют `PlatformBootstrap.AddPlatformDefaults("ServiceName")` для cross-cutting (Serilog + OTel + JWT + CORS + OpenAPI + RateLimiter). Единственное исключение — TelegramBotService, у которого `EnableObservability=false` из-за кастомной TBF Serilog инициализации.

## Кастомные бизнес-метрики (issue #67)

- `EducationPlatform.Notifications` meter — `notification_dispatch_duration_seconds`, `notification_outbox_publish_lag_seconds` (consumer-side), `notification_delivery_outcomes_total`. Класс `NotificationService.Core.Diagnostics.NotificationMetrics`, singleton + `IMeterFactory`.
- `EducationPlatform.Telegram` meter — `telegram_send_duration_seconds`, `telegram_throttle_wait_seconds`, `telegram_handler_lag_seconds`, `telegram_delivery_outcomes_total`. Класс `TelegramBotService.Core.Diagnostics.TelegramMetrics`.
- `EducationPlatform.Education` meter (#482) — `level_test_fetched_total` (верх level-test воронки: успешный `GetActiveLevelTest`, proxy «начали тест»). Класс `EducationContentService.Core.Diagnostics.EducationContentMetrics`.
- `EducationPlatform.Progress` meter (#482) — level-test воронка в ProgressService: `level_test_submitted_total` (label `subject`: anonymous/authenticated), `level_test_claimed_total` (lead-gate конверсия, += claimedCount), `level_test_ai_graded_total` (label `outcome`: ready/failed). Класс `ProgressService.Core.Diagnostics.ProgressMetrics`.
- Wolverine 4.x экспонирует built-in OTel meter `"Wolverine"` — outbox/inbox depth, handler duration, retry/DLQ counters. Регистрируется в `ObservabilityExtensions.AddObservability` через `.AddMeter(WOLVERINE_SOURCE)`.

**Правило для новых бизнес-метрик:** точечно через `IMeterFactory`, не разводи `*Diagnostics.cs` static-helpers. Имя meter'а — public const в `ObservabilityExtensions` + регистрация в `WithMetrics(...)`. Без регистрации Meter молча дропается OTel pipeline'ом.

Dashboard «Notification Pipeline» (`docker/grafana/dashboards/notification-pipeline.json`, тег `notification-pipeline`) собирает все четыре meter'а в одно место + RabbitMQ queue depths + DLQ. Ищется через Grafana MCP `search_dashboards("notification")`.

Dashboard «Growth Funnel» (`docker/grafana/dashboards/growth-funnel.json`, тег
`growth-funnel`) соединяет уже существующие серверные метрики level-test и billing:
открытие теста → анонимная отправка → claim после входа → созданный заказ → PAID.
Проценты на dashboard — оконное приближение за 24 часа, а не cohort analysis.
Браузерные acquisition/UTM-события намеренно остаются в Яндекс Метрике.

## Кастомные ActivitySource для traces

Только две: `ContentAccess` (entitlement check spans, в `Shared/ContentAccess/ContentAccess/ContentAccessDiagnostics.cs`) и `SearchService` (Typesense query spans). Остальные сервисы трассируются через built-in instrumentation (AspNetCore, EFCore, Npgsql, Wolverine, RabbitMQ.Client).

## Реализация

- `ObservabilityExtensions.AddSerilogLogging` — Console sink only (логи стримятся через Alloy → Loki)
- `ObservabilityExtensions.AddObservability` — `.AddOtlpExporter()` для traces+metrics регистрируется ТОЛЬКО если env var `OTEL_EXPORTER_OTLP_ENDPOINT` задан
- Sampling: head-based 10% prod / 100% dev на app-side; tail-based в prod collector (100% errors + 100% slow + 10% rest)
- Tempo metrics_generator: span-metrics + service-graphs → remote_write в Prometheus (для service graph view в Grafana)

## Debug order

logs (Loki) → traces by TraceId (Tempo) → metrics by service (Prometheus).

## Business dashboards over Postgres (Trainer stats)

Some business metrics live only in domain tables (no Prometheus meter). For those we
provision a **Postgres datasource** that queries the application DB directly.

- **Datasource `education-postgres`** (uid `education-postgres`, `type: postgres`,
  `url: postgres:5432`, `database: education_platform`, `sslmode: disable`,
  `postgresVersion: 1600`). Provisioned in
  `docker/grafana/provisioning/datasources/datasources.yaml` (dev) and
  `docker/grafana/provisioning-prod/datasources/datasources.yaml` (prod).
- **Dev creds** — a least-privilege **read-only** role `grafana_ro` (NOT the app
  superuser), created by `docker/postgres/init-databases.sql` on a fresh volume.
  Datasource interpolates `$GRAFANA_PG_USER` / `$GRAFANA_PG_PASSWORD` (defaults
  `grafana_ro` / `grafana_ro` in `.env.example`); the dev grafana container mounts
  `env_file: .env`, so they resolve automatically.
  - **Existing dev volumes** (init script already ran) need the role created once
    manually: `docker compose exec -T postgres psql -U postgres -d education_platform -c "DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='grafana_ro') THEN CREATE ROLE grafana_ro LOGIN PASSWORD 'grafana_ro'; END IF; END \$\$; GRANT CONNECT ON DATABASE education_platform TO grafana_ro; GRANT USAGE ON SCHEMA trainer TO grafana_ro; GRANT SELECT ON ALL TABLES IN SCHEMA trainer TO grafana_ro; ALTER DEFAULT PRIVILEGES IN SCHEMA trainer GRANT SELECT ON TABLES TO grafana_ro;"`
- **Prod creds** — placeholders `$GRAFANA_PG_USER` / `$GRAFANA_PG_PASSWORD`, intended
  for a **read-only** DB role, NOT the app superuser. `editable: false` like the other
  prod datasources.

**Dashboard `Trainer stats`** (uid `trainer-stats`, tag `trainer`, file
`docker/grafana/dashboards/trainer-stats.json`) — panels all run `rawSql` time-series
queries against the `trainer` schema with the `$__timeFilter(...)` macro so the
time-picker works:

| Panel | Table |
|---|---|
| Сессии в день (по режиму) | `trainer.training_sessions` (group by day + `mode`) |
| Активные пользователи в день | `trainer.training_sessions` (`count(distinct user_id)`) |
| Доля завершённых сессий | `trainer.training_sessions` (`COMPLETED` / started) |
| Средняя точность ответов в день | `trainer.training_session_items` (`avg(score_percent)`) |
| AI-стоимость в день (₽) | `trainer.ai_usage` (`SUM(cost_micro_rub)/1e6`) |
| AI-операции в день (по типу) | `trainer.ai_usage` (group by day + `operation`) |

> **OWNER-ACTION (prod):** create a read-only Postgres role (e.g. `GRANT USAGE ON
> SCHEMA trainer; GRANT SELECT ON ALL TABLES IN SCHEMA trainer`), store its creds in
> Infisical, and pass `GRAFANA_PG_USER` / `GRAFANA_PG_PASSWORD` into the prod grafana
> container (`docker-compose.prod.yml`). Until that is wired the `education-postgres`
> datasource resolves to empty creds and the Trainer-stats dashboard shows "no data"
> on prod. Dev works out of the box.
