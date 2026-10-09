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
- Wolverine 4.x экспонирует built-in OTel meter `"Wolverine"` — outbox/inbox depth, handler duration, retry/DLQ counters. Регистрируется в `ObservabilityExtensions.AddObservability` через `.AddMeter(WOLVERINE_SOURCE)`.

**Правило для новых бизнес-метрик:** точечно через `IMeterFactory`, не разводи `*Diagnostics.cs` static-helpers. Имя meter'а — public const в `ObservabilityExtensions` + регистрация в `WithMetrics(...)`. Без регистрации Meter молча дропается OTel pipeline'ом.

Dashboard «Notification Pipeline» (`docker/grafana/dashboards/notification-pipeline.json`, тег `notification-pipeline`) собирает все четыре meter'а в одно место + RabbitMQ queue depths + DLQ. Ищется через Grafana MCP `search_dashboards("notification")`.

Dashboard «Growth Funnel» (`docker/grafana/dashboards/growth-funnel.json`, тег
`growth-funnel`) показывает серверные метрики billing: созданные заказы, оплата и ошибки.
Проценты на dashboard — оконное приближение за 24 часа, а не cohort analysis.
Браузерные acquisition/UTM-события намеренно остаются в Яндекс Метрике.

## Кастомные ActivitySource для traces

Одна: `ContentAccess` (entitlement check spans, в `Shared/ContentAccess/ContentAccess/ContentAccessDiagnostics.cs`). Остальные сервисы трассируются через built-in instrumentation (AspNetCore, EFCore, Npgsql, Wolverine, RabbitMQ.Client).

## Реализация

- `ObservabilityExtensions.AddSerilogLogging` — Console sink only (логи стримятся через Alloy → Loki)
- `ObservabilityExtensions.AddObservability` — `.AddOtlpExporter()` для traces+metrics регистрируется ТОЛЬКО если env var `OTEL_EXPORTER_OTLP_ENDPOINT` задан
- Sampling: head-based 10% prod / 100% dev на app-side; tail-based в prod collector (100% errors + 100% slow + 10% rest)
- Tempo metrics_generator: span-metrics + service-graphs → remote_write в Prometheus (для service graph view в Grafana)

## Debug order

logs (Loki) → traces by TraceId (Tempo) → metrics by service (Prometheus).
