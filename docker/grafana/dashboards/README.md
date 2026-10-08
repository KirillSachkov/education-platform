# Grafana Dashboards

Provisioned dashboards платформы. Grafana при старте сканирует этот каталог
(`provisioning/dashboards/dashboards.yaml` → `path: /var/lib/grafana/dashboards`)
и автоматически (re)provision'ит все `*.json` файлы. Удалил файл → dashboard
исчезнет на следующем reload'е.

## Текущий набор

### Core (5)

| Файл | UID | Назначение |
|---|---|---|
| `logs.json` | `logs-explorer` | Logs Explorer. Variables: `$service`, `$level`, `$search`. Volume + error timeline + live tail. Главная точка входа когда нужно «посмотреть что в логах сервиса X» |
| `errors.json` | `errors-overview` | Sentry-like view: total errors, error rate, errors by service, error timeline, error log stream с авто-клик-через в Tempo через TraceID derived field |
| `platform-health.json` | `platform-health` | RED метрики (Rate / Errors / Duration). RPS, error %, P50/P95/P99, status codes mix, RPS+errors per service. Главная точка для «что сейчас с платформой» |
| `traces.json` | `traces-explorer` | TraceQL search + service graph (auto-built из span'ов через Tempo metrics-generator) + cheatsheet. Главная точка для «откуда задержка / где упало» |
| `notification-pipeline.json` | `notification-pipeline` | End-to-end notification dispatch + telegram delivery. Outbox publish lag, TG send duration, throttle wait, delivery outcomes, Wolverine handler latency, queue depth + DLQ |

### Phase 2: Infrastructure (4)

| Файл | UID | Назначение |
|---|---|---|
| `postgres.json` | `postgres-platform` | PG-exporter: connections, saturation %, DB size, commits/rollbacks, tuples ops, deadlocks (24h), shared_buffers cache hit ratio, locks |
| `redis.json` | `redis-platform` | Redis-exporter: memory used/max, hit rate, evicted keys (CRITICAL=0 при noeviction), connected clients, ops/s, top 10 commands by p99 latency |
| `rabbitmq.json` | `rabbitmq` | RMQ-exporter: ready/unacked per queue, publish/deliver/ack rate, consumers per queue, zero-consumer alarm, used memory, **DLQ depth (`wolverine-dead-letter-queue`)** |
| `http-endpoints.json` | `http-endpoints` | Drill-down по HTTP route: req rate, slowest endpoints top-10 (p95), status code mix (pie), error rate per endpoint (table) |

### Phase 3: Per-service drill-down (#89)

| Файл | UID | Назначение |
|---|---|---|
| `dotnet-runtime.json` | `dotnet-runtime` | .NET Runtime metrics. Templated `$service`. CPU rate, working set, GC heap by gen + collections rate + pause time fraction (% wall time) + allocation rate, thread pool threads/queue, lock contentions, exceptions by type, JIT IL/sec. Источник — `System.Runtime` meter (OTel `AddRuntimeInstrumentation`) |
| `service-detail.json` | `service-detail` | Per-service RED + Wolverine + HTTP outbound. Templated `$service` (single-select). RPS / 5xx % / p95 / DLQ; top routes by RPS / latency / errors; Wolverine outbox/inbox/scheduled depth, throughput, handler exec p95 by message_type, top failures; HttpClient outbound RPS / p95 / errors / connections. Health-эндпоинты исключены из RPS/latency панелей |

### Business pipelines

| Файл | UID | Назначение |
|---|---|---|
| `growth-funnel.json` | `growth-funnel` | Серверно подтверждённая воронка: открытие теста уровня → анонимная отправка → claim после входа → создание заказа → подтверждённая оплата. Конверсии считаются как приближение в окне 24h; acquisition/UTM остаются в Яндекс Метрике |
| `payments-pipeline.json` | `payments-pipeline` | T-Bank billing: создание заказа, Init API, webhook outcomes, PAID/FAILED и reconciliation |
| `onboarding-pipeline.json` | `onboarding-pipeline` | ⚠️ **WIP — feature #68 не в main.** Plan Onboarding (GitHub App): invitation outcomes per minute, success rate, webhook signature verification, GitHub API duration, token cache hit ratio, install state store. Метрики `onboarding_*` начнут приходить только после merge `feature/plan-onboarding-68` в main + первого реального GitHub App install. До этого dashboard рендерит «no data» на проде. Пара panel'ов опираются на `pg_stat_user_tables_n_live_tup` (approximate, autovacuum-dependent) — точнее cделать через bus-based gauge при необходимости |

## Корреляция между signals

Настроена в `provisioning/datasources/datasources.yaml`:

- **Loki → Tempo:** `derivedFields` парсит `TraceId` в JSON логах + `trace_id=...` substring → клик в логе открывает trace в Tempo
- **Tempo → Loki:** `tracesToLogsV2` → клик на span открывает связанные логи (фильтр по `service_name + trace_id`)
- **Tempo → Prometheus:** `tracesToMetrics` → клик на span открывает RED-метрики службы за окно ±2m

## Стек источников данных

| Datasource | UID | Что хранит | Как пополняется |
|---|---|---|---|
| **Loki** | `loki` | логи всех контейнеров | Alloy читает Docker stdout → парсит JSON (level, trace_id, http метаданные) → push в Loki |
| **Tempo** | `tempo` | traces + service-graph metrics | .NET-сервисы шлют OTLP → otel-collector → Tempo. Tail-sampling в prod (errors+slow+10%) |
| **Prometheus** | `prometheus` | metrics | otel-collector экспортит app metrics на `:8889`, exporters (postgres/redis/rabbitmq) скрейпятся напрямую. Tempo metrics-generator пишет span-metrics через remote_write |

## Запуск локально

```bash
./scripts/dev.sh up-obs   # единственная команда, поднимающая весь Grafana stack
open http://localhost:3001
```

В `.env` для obs включён anonymous Editor (`GF_AUTH_ANONYMOUS_ENABLED=true`,
`GF_AUTH_ANONYMOUS_ORG_ROLE=Editor`) — авторизация локально не нужна, можно
сразу редактировать дашборды.

## Что добавлять, когда понадобится

- **бизнес-метрики** — добавить custom `Meter` в use-case'ах (напр. `enrollments_created_total`), потом панель в `platform-health.json`. Pattern: см. `NotificationMetrics` / `TelegramMetrics` + регистрация в `ObservabilityExtensions.AddObservability` (`AddMeter(...)`)
- **wolverine outbox depth** — Wolverine 4.x emits `wolverine_*` metrics через built-in OTel meter. Готова панель в `notification-pipeline.json`. Для общего outbox depth — добавить custom postgres-exporter query на `wolverine_outgoing_envelopes` count
- **alerts** — Phase 3, через Grafana Alerting → webhook → NotificationService или прямо в TelegramBotService
- **frontend RUM** — Phase 4 (если решим). Faro SDK в Next.js → отдельный dashboard

## Edit workflow

1. В Grafana UI редактируешь dashboard (anonymous Editor пускает)
2. Save dashboard → JSON model → копируешь содержимое
3. Заменяешь JSON в `docker/grafana/dashboards/{name}.json`
4. Коммитишь — следующий старт Grafana подхватит из файла
5. **UID не меняй** — иначе плодятся дубликаты
