# Runbook: Disaster Recovery

Оперативные процедуры для аварийных ситуаций с данными. Команды применяйте к выбранному окружению. Примеры используют `PROD_SSH_HOST` и демонстрационные идентификаторы.

## Содержание

- [1. Потеря Redis (flush / new cluster)](#1-потеря-redis)
- [2. Дрейф user-grants](#2-дрейф-user-grants)
- [3. Импорт / seeding данных на прод](#3-импорт--seeding-данных-на-прод)
- [4. Stuck Wolverine outgoing envelopes](#4-stuck-wolverine-outgoing-envelopes)
- [5. Расшифровка симптомов](#5-расшифровка-симптомов)
- [SearchService auto-reindex — force и troubleshooting](#searchservice-auto-reindex--force-и-troubleshooting)
- [SearchService lifecycle bindings cutover](#searchservice-lifecycle-bindings-cutover)
- [ProgressService lifecycle bindings cutover](#progressservice-lifecycle-bindings-cutover)

---

## SearchService lifecycle bindings cutover

`search.education.lifecycle_events` использует только 36 exact routing keys, для которых
в SearchService есть обработчики. Wolverine создаёт новые bindings, но не удаляет старые
durable wildcard-связи. После первой выкладки exact-конфигурации удалить только шесть
legacy bindings; очередь и накопленные сообщения не удалять.

```bash
# Проверить топологию до изменения.
docker exec rabbitmq sh -lc 'rabbitmqadmin -u "$RABBITMQ_DEFAULT_USER" -p "$RABBITMQ_DEFAULT_PASS" \
  list bindings' \
  | grep 'education.events' | grep 'search.education.lifecycle_events'

for key in '*.created' '*.updated' '*.published' '*.restored' '*.soft_deleted' '*.hard_deleted'; do
  docker exec rabbitmq sh -lc "rabbitmqadmin -u \"\$RABBITMQ_DEFAULT_USER\" -p \"\$RABBITMQ_DEFAULT_PASS\" \
    delete binding --source education.events --destination-type queue \
    --destination search.education.lifecycle_events --routing-key '$key' --idempotently"
done

# После удаления wildcard'ов должны остаться exact keys из
# RabbitMqConfiguration.EducationLifecycleRoutingKeys.
docker exec rabbitmq sh -lc 'rabbitmqadmin -u "$RABBITMQ_DEFAULT_USER" -p "$RABBITMQ_DEFAULT_PASS" \
  list bindings' \
  | grep 'education.events' | grep 'search.education.lifecycle_events'
```

Повторный запуск безопасен благодаря `--idempotently`.

---

## ProgressService lifecycle bindings cutover

При переходе очереди `progress.education.lifecycle_events` с wildcard routing keys на
exact bindings одного обновления кода недостаточно. RabbitMQ хранит durable
bindings, а Wolverine не удаляет ключи, которые исчезли из конфигурации.

После выкладки ветки с exact bindings один раз удалить старые связи. Саму
очередь не удалять: в ней могут быть сообщения, а exact bindings уже созданы
стартом ProgressService.

```bash
# 1. Сначала проверить топологию.
docker exec rabbitmq sh -lc 'rabbitmqadmin -u "$RABBITMQ_DEFAULT_USER" -p "$RABBITMQ_DEFAULT_PASS" \
  list bindings' \
  | grep 'education.events' | grep 'progress.education.lifecycle_events'

# 2. Удалить только четыре legacy wildcard bindings. Команды не трогают
#    очередь и накопленные сообщения.
for key in '*.created' '*.soft_deleted' '*.restored' '*.hard_deleted'; do
  docker exec rabbitmq sh -lc "rabbitmqadmin -u \"\$RABBITMQ_DEFAULT_USER\" -p \"\$RABBITMQ_DEFAULT_PASS\" \
    delete binding --source education.events --destination-type queue \
    --destination progress.education.lifecycle_events --routing-key '$key' --idempotently"
done

# 3. Повторная проверка: должны остаться только exact keys
#    course.created, issue.published и hard_deleted для material/module/issue/course/quiz.
docker exec rabbitmq sh -lc 'rabbitmqadmin -u "$RABBITMQ_DEFAULT_USER" -p "$RABBITMQ_DEFAULT_PASS" \
  list bindings' \
  | grep 'education.events' | grep 'progress.education.lifecycle_events'
```

Повторный запуск безопасен: `--idempotently` не считает уже удалённый
binding ошибкой.

---

## 1. Потеря Redis

**Сценарии:** `FLUSHALL`, рестарт контейнера без persistent volume, новый кластер, corrupted AOF.

**Симптомы:**
- Все запросы материалов/заданий возвращают 401/403
- Залогиненные пользователи не видят купленный контент
- `redis-cli DBSIZE` возвращает 0 или близко

**Проверка:**
```bash
ssh root@prod
docker exec redis-platform redis-cli DBSIZE
# Если маленькое число — Redis потерян
```

**Восстановление:**

Postgres — источник правды. Redis — производный индекс. Ресинхнируем Redis из Postgres:

```bash
# 1. Убедиться что Postgres цел
docker exec postgres psql -U postgres -d education_platform \
  -c "SELECT COUNT(*) FROM education.materials WHERE status = 'PUBLISHED';"

# 2. Ресинк resource-тегов (материалы + задания)
docker exec education-service \
  dotnet EducationContentService.Web.dll resync-access-tags

# 3. Ресинк user-grants (plan-tags) — через AccessService BackfillRedisFromGrantsCli.
# Phase E (#45): legacy course-tags больше не пишутся, ProgressService resync удалён.
docker exec access-service \
  dotnet AccessService.Web.dll backfill-redis-from-grants
```

**Валидация:**
```bash
# PUBLIC материал должен открываться без авторизации
curl -i http://localhost/api/materials/<public-material-id>/detail/ | head -1
# Ожидание: 200

# Проверить что теги появились
docker exec redis-platform redis-cli SMEMBERS "resourceaccess:material:<id>"
# Ожидание: содержит "access:public", "authenticated", "plan:all" или "plan:course:X"
```

**Сколько это занимает:**
- тысячи материалов → секунды
- десятки тысяч plan-grants → до минуты

---

## 2. Дрейф user-grants (plan-tags)

**Сценарии:** юзер пожаловался "не вижу курс на который подписан", хотя `plan_grant` в БД есть.

**Проверка в Postgres:**
```bash
docker exec postgres psql -U postgres -d education_platform -c "
  SELECT user_id, plan_id, status, source
  FROM access.plan_grants
  WHERE user_id = '<user-id>' AND status = 'ACTIVE';"
```

**Проверка в Redis:**
```bash
docker exec redis-platform redis-cli SMEMBERS "usergrants:<user-id>"
```

Если в Postgres есть ACTIVE plan_grant, а в Redis нет соответствующего `plan:all` / `plan:course:X` — дрейф (Phase E #45: ProgressService больше не пишет course-tags, plan-tags — ответственность AccessService). (`plan:free:author_X` тег удалён в #358 вместе с FREE-планом; `plan:lifetime:author_X` читается только как legacy.)

**Восстановление:**
```bash
docker exec access-service \
  dotnet AccessService.Web.dll backfill-redis-from-grants
```

Команда идемпотентна, читает все ACTIVE plan_grants и приводит Redis в соответствие.

**Первопричина:** скорее всего пропавшее Wolverine-событие или бажный handler в AccessService self-consume sync (queue `access.content_access.sync`). Проверь DLQ.

---

## 3. Импорт / seeding данных на прод

### ❌ НЕ ДЕЛАЙ ТАК

**Прямые SQL `INSERT` в `education.*` / `progress.*` таблицы** — запрещено. Это обходит domain-events → Redis не получит теги → материалы/задания 401 для всех.

Исключение: разовая миграция схемы (EF Core migration) — она идёт через миграционный pipeline, не затрагивает данные.

### ✅ ПРАВИЛЬНО

**1. Single entity** — через существующие author-endpoints:
```
POST /materials/ (создать материал)
POST /courses/{id}/enrollments/ (зачислить юзера)
```
Работают runtime event handlers → Redis засинкается автоматически.

**2. Demo/seed окружение** — CLI seeder в сервисе:
```bash
docker exec education-service \
  dotnet EducationContentService.Web.dll seed-demo-curriculum
```
Seeder вызывает use-cases через домен, так что event handlers срабатывают.

**3. Bulk import специфичной предметной области** — admin-endpoint в `Core/Features/Import/`:
- Требует role `platform-admin`
- Принимает JSON/CSV в теле запроса
- Создаёт aggregates через доменные фабрики
- Event handlers синкают Redis автоматически

Не существует для твоего use-case? — написать на 30-60 строк, это дешевле чем месяц отлавливать рассинхроны после SQL-импорта.

### Если всё же влили SQL обходным путём

Ресинкнуть Redis вручную:
```bash
# после SQL INSERT/UPDATE в education.materials:
docker exec education-service \
  dotnet EducationContentService.Web.dll resync-access-tags

# после SQL INSERT/UPDATE в access.plan_grants:
docker exec access-service \
  dotnet AccessService.Web.dll backfill-redis-from-grants
```

---

## 4. Stuck Wolverine outgoing envelopes

### Симптом
- TG-уведомления не доходят (или другие cross-service события), но сайт-инбокс наполняется (InApp работает локально без RabbitMQ).
- В `{schema}.wolverine_outgoing_envelopes` накопились envelope'ы с `attempts = 0` и `owner_id != 0` (нода-владелец).
- В логах сервиса нет ни exception, ни broker disconnect. Healthcheck зелёный.
- Issue #20.

### Постоянный fix (раскатан 2026-05-02, расширен 2026-05-18)
`backend/Shared/Messaging/RabbitMqMessaging/PlatformWolverineDefaults.cs` подключён во всех 12 сервисах с Wolverine. Включает:
- **`Durability.Mode = DurabilityMode.Solo`** (#67, 2026-05-18) — корень burst-publish bottleneck'а. Default `Balanced` оптимизирован для multi-node deployment (координация ownership через `wolverine_nodes` на каждый envelope). На single-replica проде это лишний overhead, сериализующий sender pipeline. Solo mode: «All known agents will automatically start on the local node. The recovered inbox/outbox messages will start functioning immediately».
- `Durability.OutboxStaleTime = 5s` (defensive safety net; с Solo mode stuck envelope'ы практически не появляются).
- `Durability.InboxStaleTime = 10min` (симметрично для inbox).
- `ConfigureChannelCreation { PublisherConfirmationsEnabled=true, PublisherConfirmationTrackingEnabled=true }`.
- `SendingFailure.PauseSending(30s)` на `BrokerUnreachableException` + `ScheduleRetry(1s,5s,30s)` на остальные.

В норме после fix'а stuck envelope'ы не появляются вообще. Если все же увидел проблему — сначала проверь, что fix задеплоился (выгребни `wolverine_outgoing_envelopes` старше 5 минут с `attempts = 0`).

### ⚠️ Перед scale-out до multi-replica — ОБЯЗАТЕЛЬНО

`DurabilityMode.Solo` **несовместим** с 2+ репликами одного сервиса. На каждой реплике durability agent claim'ит envelope'ы локально, не координируясь через `wolverine_nodes` — две реплики одновременно отправят один и тот же envelope в RabbitMQ, дублируя events на consumer'ах.

Consumer'ы защищены `correlation_id` unique partial index'ами (`notifications.ux_notifications_correlation`, AS `plan_grants` unique constraint и т. п.), но не все handler'ы (например, fan-out NotificationCreated без stable correlation_id) → возможны duplicate side-effects (двойная TG-доставка, двойной email).

**Прежде чем поставить `replicas: 2+` в docker-compose / k8s manifest:**

1. Открой `backend/Shared/Messaging/RabbitMqMessaging/PlatformWolverineDefaults.cs`.
2. Замени `opts.Durability.Mode = DurabilityMode.Solo;` на `opts.Durability.Mode = DurabilityMode.Balanced;`.
3. Убедись, что у scaled сервиса корректно работает node-assignment dance: `wolverine_nodes` таблица заполняется живыми heartbeat'ами, ownership transfers идут через DurabilityAgent.
4. Соответственно вернётся multi-node coordination overhead на burst publish — измерь latency на dev/staging прежде раскатки.

Альтернатива — оставить Solo, но scale-out не сервиса целиком, а только consumer-listener'ов (через `MaximumParallelMessages(N)` на конкретных queue'ах). Это уже сделано для `telegram_bot.notifications.delivery_events` (N=10).

### Manual recovery (если нужно немедленно)

```sh
# Сначала диагностика
ssh "${PROD_SSH_HOST:?set PROD_SSH_HOST}"
docker exec postgres psql -U platform -d education_platform -c "
  SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE '%';
"

# Подсчёт stuck per-schema (заменить {schema} на конкретное имя):
docker exec postgres psql -U platform -d education_platform -c "
  SELECT COUNT(*), owner_id FROM {schema}.wolverine_outgoing_envelopes
  WHERE attempts = 0 GROUP BY owner_id;
"

# Если есть — освободить ownership. Recovery sweep подберёт за 60s.
docker exec postgres psql -U platform -d education_platform -c "
  UPDATE {schema}.wolverine_outgoing_envelopes SET owner_id = 0 WHERE attempts = 0;
"
```

Если sweep сам сломан — `docker restart {service-name}` (новая нода видит старый `owner_id` как stale → recovery).

### Проверка после
```sh
# через 60-120 секунд
docker exec postgres psql -U platform -d education_platform -c "
  SELECT COUNT(*) FROM {schema}.wolverine_outgoing_envelopes;
"
# должно быть 0 или близко к 0
```

---

## SearchService auto-reindex — force и troubleshooting

С issue #265 SearchService сам перекатывает Typesense при изменении схемы / денорм-логики. Disaster-recovery — в трёх ситуациях:

### Симптом: «индекс пустой / сломан, но `applied_generation == ReindexGeneration`»

Стандартная процедура — `ForceReindexOnStartup`:

```sh
ssh "${PROD_SSH_HOST:?set PROD_SSH_HOST}"
# выставить флаг через env (Infisical или docker-compose env)
# рестарт search-service
docker restart search-service

# проверить, что startup-реиндекс стартовал
docker logs --since 2m search-service 2>&1 | grep -iE "(reindex|Force)"
# ожидаем:
#   "Search reindex auto-triggered on startup ... Force: true"
#   "Full search reindex completed ... AppliedGeneration: <N>"

# ⚠️ СНЯТЬ ForceReindexOnStartup ОБРАТНО (в false)
# иначе реиндекс будет крутиться каждый рестарт контейнера
```

Альтернатива (если не хочется трогать конфиг) — админская кнопка из UI или curl:

```sh
# нужен токен с ролью platform-admin
curl -X POST -H "Authorization: Bearer $TOKEN" \
  https://sachkov-learn.net/api/search/admin/reindex
```

### Симптом: «деплой со схема-апдейтом прошёл, но реиндекс не стартанул»

Проверь, что `ReindexGeneration` действительно бампнулся:

```sh
docker exec postgres psql -U platform -d education_platform -c "
  SELECT applied_generation, last_applied_at_utc, last_request_id
  FROM search.reindex_state;
"

docker exec search-service env | grep -i reindex
# или из конфига:
docker exec search-service cat /app/appsettings.Production.json | grep -A1 ReindexGeneration
```

Если `config > applied_generation` — что-то пошло не так на старте сервиса. Логи:

```sh
docker logs --since 5m search-service 2>&1 | grep -i reindex
```

Если ничего полезного — поднять руками через `ForceReindexOnStartup=true` (выше).

### Симптом: «реиндекс крутится каждый рестарт»

Скорее всего:
- `ForceReindexOnStartup=true` забыли снять;
- handler падает в середине → `applied_generation` не обновляется.

```sh
# найти последний завершённый
docker exec postgres psql -U platform -d education_platform -c "
  SELECT applied_generation, last_applied_at_utc FROM search.reindex_state;
"

# если last_applied_at_utc старый/NULL — handler падает
docker logs --since 30m search-service 2>&1 | grep -iE "(Full search reindex (failed|completed))"
```

Если handler падает — диагностика по логам ошибки. Реиндекс зависит от:
- `IEducationContentServiceClient.ExportAllSearchEntitiesAsync` (ECS должен отвечать),
- `ITagServiceClient.GetEntitiesTagsSearchLookupAsync` (TagService должен отвечать),
- Typesense alias swap (нужно место на диске Typesense).

---

## 5. Расшифровка симптомов

| Симптом | Вероятная причина | Куда копать |
|---|---|---|
| Все `/materials/*/detail/` → 401 | Redis flushed | Проверка 1, resync-access-tags |
| Один юзер не видит курс | Дрейф user-grants | Проверка 2, backfill-redis-from-grants (AccessService) |
| PUBLIC материал открывается у авторизованного, но не у анонима | Backend нарушил fail-closed | Проверить логи `AccessReason=NOT_AUTHENTICATED` в Loki |
| `AccessReason=RESOURCE_NOT_REGISTERED` в логах | Материал не зарегистрирован в Redis | resync-access-tags |
| OOM в Redis | Переполнение | Увеличить `--maxmemory` в docker-compose.prod.yml, рестарт |
| Redis restart и всё пропало | Отключилась персистенция | Проверить наличие `redis_data` volume и флагов `--appendonly yes` |
| TG-уведомления не доходят, сайт-инбокс полный | Stuck Wolverine outbox | Проверка 4, `UPDATE owner_id=0` если recovery sweep не отработал |

---

## Архитектурные инварианты

1. **Postgres — единственный источник правды.** Redis — производный кеш/индекс, восстанавливаемый из Postgres.
2. **Runtime event handlers** (Wolverine) поддерживают Redis актуальным на write-path. Если они работают — ресинк не нужен.
3. **Ресинк-CLI — только emergency.** Никакой автоматизации (ни периодической, ни на startup). Если понадобилось запустить — была аномалия, логируй и разбирайся в первопричине.
4. **Redis персистентен** (AOF + RDB volumes в docker-compose). Рестарт контейнера не теряет данные.
5. **`maxmemory-policy = noeviction`.** Нельзя выкидывать теги доступа под нагрузкой — лучше отказать записи. Плановое увеличение memory при приближении к лимиту.

---

## Мониторинг (TODO)

Пока недоступно — добавить после Grafana-setup:
- Dashboard "Redis health": `redis_memory_used`, `redis_aof_last_write_status`, `redis_connected_clients`
- Alert: `redis_db_size` резко упал → Redis потерян
- Alert: `AccessReason=RESOURCE_NOT_REGISTERED` count растёт → кто-то создаёт материалы в обход домена


---

## 6. GitHub App для plan onboarding (#68)

Plan onboarding wizard опирается на GitHub App для автоматических org-приглашений.
App регистрируется один раз на github.com и устанавливается каждым автором в свою org.

### Регистрация App (one-time, выполняется владельцем платформы)

1. На github.com → Settings → Developer settings → **GitHub Apps** → New GitHub App.
2. Name (slug): например, `sachkov-learn-org-bridge`. **Public** (any account can install).
3. Homepage URL: `https://platform/`. User-authorization: не нужен (используем install-flow).
4. **Permissions:** Organization → **Members: Read and write**. Никаких repo-permissions.
5. **Subscribe to events:** `organization`, `installation`.
6. **Webhook URL:** `https://platform/api/access/webhooks/github`.
7. **Webhook secret:** `openssl rand -hex 32` → вставить в форму И в Infisical (`GITHUBAPP__WEBHOOKSECRET`).
8. Создать App → запомнить **App ID**, **Client ID**, **Slug**.
9. Сгенерировать private key (Generate a private key) → `.pem` файл.
   `base64 -i private-key.pem | tr -d '\n'` → положить в Infisical (`GITHUBAPP__PRIVATEKEYPEMBASE64`).
10. Заполнить остальные env vars: `GITHUBAPP__APPID`, `GITHUBAPP__SLUG`, `GITHUBAPP__CLIENTID`,
    `GITHUBAPP__FRONTENDINSTALLRETURNURL=/author/plans` (fallback если автор install-flow без planId).

### Health-check после деплоя

```bash
ssh root@prod
# 1. AccessService подхватил env vars
docker exec access-service env | grep GITHUBAPP__ | head

# 2. Логи AccessService при первом install — должно быть «GitHub App installed by author=…»
docker logs access-service 2>&1 | grep -i "GitHub App installed"

# 3. Webhook delivery проверка
# На github.com → App → Advanced → Recent Deliveries — статусы должны быть 200.
# 401 = signature mismatch (проверь WebhookSecret); 503 = secret не задан вообще.
```

### Симптомы и реакция

| Симптом | Причина | Реакция |
|---|---|---|
| `App JWT signing failed` в логах | Invalid PEM в `GITHUBAPP__PRIVATEKEYPEMBASE64` | Проверь base64 (без переносов), regenerate ключ на github.com если нужно |
| `GitHub webhook signature mismatch` | Webhook secret разъехался между Infisical и github.com | Сверь оба места, restart access-service |
| `installation_token failed with 401` | Installation удалена автором, либо App suspended | См. `author_github_installations.suspended_at`. Автор должен переустановить App |
| `installations/{id} → 404` в логах | Installation_id хранится, но App уже удалён в org | Очистить запись: `DELETE FROM access.author_github_installations WHERE installation_id=…` |
| Pending invitations stuck >7d | GitHub-side invitation expired (default 7 days) | Юзер сам жмёт «Запросить новое приглашение» в onboarding wizard. Cron на эту автоматизацию не нужен |
| Authors массово получают `no_installation` failures | Authors не подключили App после deploy | Email broadcast с инструкцией; кнопка «Подключить GitHub App» в `/author/plans/{id}/edit` |

### Ротация private key

Каждые 12 месяцев (security best-practice) либо при подозрении на compromise:

```bash
# 1. На github.com → App → Private keys → Generate a private key.
#    Старый ключ остаётся валидным — у App может быть до 25 ключей одновременно.
# 2. Encode + положить в Infisical:
base64 -i new-key.pem | tr -d '\n' | pbcopy
# Обновить GITHUBAPP__PRIVATEKEYPEMBASE64 в Infisical.
# 3. Redeploy AccessService → подхватит новый ключ.
docker compose up -d access-service
# 4. Smoke — install-flow / invitation create должен работать.
# 5. Через 24h: на github.com удалить старый key.
```

### Disaster recovery: потеряны все installation записи

Если `access.author_github_installations` пустая (например, корраптилось или ALTER TABLE снёс):

```bash
# Авторам надо переустановить App. Нет автоматического способа восстановить
# installation_id — нужно вручную из GitHub Settings → App → Installations
# (там список всех org с installation_id) и SQL INSERT'ом.
ssh root@prod
docker exec -it postgres psql -U postgres -d education_platform
```
```sql
INSERT INTO access.author_github_installations
  (author_id, installation_id, org_login, installed_at)
VALUES
  ('<author-uuid>', 12345678, 'org-name', NOW())
ON CONFLICT (author_id) DO UPDATE SET
  installation_id = EXCLUDED.installation_id,
  org_login = EXCLUDED.org_login,
  installed_at = NOW(),
  suspended_at = NULL;
```

Pending invitations можно либо оставить (юзер сам пере-запросит) либо массово отменить:

```sql
UPDATE access.github_org_invitations
SET status = 'CANCELED', last_synced_at = NOW()
WHERE status = 'PENDING';
```

### Onboarding flow stuck

**Юзер залип в gate-петле «всегда видит wizard»:**

```sql
-- 1. Найти онбординг
SELECT * FROM access.user_plan_onboardings WHERE user_id = '<user-uuid>';

-- 2a. Force-complete (если юзер заслужил пройти):
UPDATE access.user_plan_onboardings
SET completed_at = NOW(), current_step_id = NULL
WHERE user_id = '<user-uuid>' AND plan_id = '<plan-uuid>';

-- 2b. Force-reset (юзер хочет пройти заново):
DELETE FROM access.user_plan_onboardings
WHERE user_id = '<user-uuid>' AND plan_id = '<plan-uuid>';
-- Следующий refetch /access/onboarding/current/ создаст заново ТОЛЬКО если flow.is_enabled=true.
```

**Автор хочет принудительно прогнать всех учеников через обновлённый онбординг:**

Default behavior — НЕ ретриггерим (см. spec). Если автор настаивает:

```sql
-- осторожно: затронет N юзеров
UPDATE access.user_plan_onboardings
SET completed_at = NULL, started_at = NOW(), current_step_id = NULL,
    skipped_step_ids = '{}', completed_step_ids = '{}'
WHERE plan_id = '<plan-uuid>';
```

Ученики получат wizard заново на следующий заход.

---

## 7. AssignmentReviewService — re-index ref-repo / cleanup stuck reviews (#15)

### Симптом

- Студент жмёт «Запустить AI-проверку», iteration падает с `review.no_installation` хотя GitHub App установлен.
- AI ревью использует устаревшие гайдлайны автора (ProjectReviewContext changed but ARS context_documents stale).
- Несколько `AiReview` в `Status=RUNNING` дольше TimeoutSeconds — обычно после рестарта pod в момент LLM-вызова.

### Re-index ref-repo

Если автор поменял `RefRepoUrl` или `GuidelinesMarkdown` и ARS не перепроиндексировал — обычно problem с consumer queue. Проверь:

```bash
docker exec rabbitmq rabbitmqadmin -u guest -p guest get queue=assignment_review.education.review_context count=1
docker exec rabbitmq rabbitmqadmin -u guest -p guest get queue=assignment_review.education.review_context_snapshot count=1
```

Если очереди пустые но индекс stale — manually publish event через ECS endpoint или прямо в БД:

```sql
-- Что есть для проекта в RAG store
SELECT owner_type, owner_id, count(*)
FROM assignment_review.context_documents
WHERE owner_id = '<project-uuid>'
GROUP BY 1, 2;
```

Удалить stale chunks + cascade на documents (FK ON DELETE CASCADE):

```sql
DELETE FROM assignment_review.context_documents
WHERE owner_type = 'PROJECT' AND owner_id = '<project-uuid>';
```

После delete — автор снова сохраняет `ProjectReviewContext` в админке → событие → re-index.

### Cleanup stuck `RUNNING` AiReviews

```sql
-- Сколько висит в RUNNING больше 10 минут
SELECT id, submission_id, repo_full_name, pull_number, updated_at, iterations_count
FROM assignment_review.ai_reviews
WHERE status = 'RUNNING'
  AND updated_at < NOW() - INTERVAL '10 minutes';
```

Reset в QUEUED (студент сможет перезапустить):

```sql
UPDATE assignment_review.ai_reviews
SET status = 'QUEUED', updated_at = NOW()
WHERE status = 'RUNNING'
  AND updated_at < NOW() - INTERVAL '10 minutes';
```

`ai_review_iterations` без `completed_at` остаются как PROCESSING — они завершатся следующим run-iteration (новый StartIteration создаст новый row). Этот мусор можно подчистить вручную если нужно:

```sql
DELETE FROM assignment_review.ai_review_iterations
WHERE status = 'PROCESSING' AND completed_at IS NULL
  AND started_at < NOW() - INTERVAL '1 hour';
```

### Cache invalidation после ручного UPDATE на `ai_model_settings`

Resolver кэширует singleton 60 секунд. Если ты manually patch'ил DB row — wait 60s или рестартни ARS pod.

### Ключевые dashboard panels

`Assignment Review Pipeline` (Grafana → search `assignment-review-pipeline`):
- Iteration outcomes — пик любого `review.*` failure-кода = upstream issue (GitHub / LLM down).
- RAG context chunks — резкое падение к 0 = индекс stale или embedding-провайдер недоступен.
- Indexing runs (1h) — failed > 0 = нужен re-index manual.
---

## 9. Revisioned media binding cutover

The FileService/AuthService/EducationContentService media-binding schema is a coordinated
release boundary. Release metadata retained by the trusted manual GitHub production workflow contains
`MEDIA_BINDING_PROTOCOL=1`.

On the first cutover, the production deploy job:

1. Creates a pre-deploy PostgreSQL backup.
2. Pulls all immutable service and migration images.
3. Stops the old `file-service`, quiescing bind/upload completion writes. It remains stopped on
   failure because old FileService code must not write against a partially advanced protocol.
4. Runs AuthService and EducationContentService migrations, then recreates only those two
   revision-aware services and waits for both health checks. PostgreSQL, RabbitMQ, and the other
   application containers remain running.
5. Runs the FileService migration only after the revision-aware Auth/Education services are
   healthy, then runs the remaining migration services from the selected release inventory sequentially. Any failure stops the
   deploy immediately.
6. Starts the complete application revision. Compose re-runs the idempotent migration services
   as dependency gates before their corresponding applications start.
7. Promotes `pending.env` to `current.env` only after compose startup succeeds; the normal
   health and public smoke checks still gate the deployment.

The deploy must never manually start containers left in `Created` state after a failed migration.
That bypasses `service_completed_successfully` and can run application code against an older
schema. A failed preflight or compose startup leaves `pending.env` unpromoted for diagnosis and
retry.

After both the current and target releases carry `MEDIA_BINDING_PROTOCOL=1`, the special cutover
is no longer needed: all migration services in the selected release inventory run as a fail-fast preflight while the
current application revision remains online, followed by the full compose recreation.

### Rollback boundary

The automated rollback job deliberately refuses both completed and partial protocol transitions:

```text
current.env or pending.env: MEDIA_BINDING_PROTOCOL=1
previous.env:               marker absent
```

Old images do not understand prepared/confirmed/detached revisions. Running them against the
migrated database can reintroduce stale-message deletion races. If the first cutover fails after
migrations, use one of these recovery paths:

- preferred: deploy a forward fix with `MEDIA_BINDING_PROTOCOL=1`;
- disaster recovery: stop application writes, restore the pre-deploy database backup and then
  deploy the matching pre-marker images.

After both `current.env` and `previous.env` carry the marker, ordinary image rollback is allowed.
Rollback compose startup is fail-closed too: a failed migration or dependency never triggers a
manual start of containers left in `Created`, and release metadata remains unchanged.
The FileService migration `Down` also refuses to collapse revisions while prepared and confirmed
values differ; drain/reconcile outstanding bindings before any manual schema downgrade.
