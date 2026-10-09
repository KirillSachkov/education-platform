# AssignmentReviewService — локальное тестирование

Сервис требует отдельной готовности к production, но локально работает полностью. Текущую задачу ищи в активном private tracker по [правилам проекта](agents/issue-tracker.md). Этот гайд — путь от чистого checkout'а до end-to-end AI-ревью реального PR. См. также: [backend/AssignmentReviewService/CLAUDE.md](../backend/AssignmentReviewService/CLAUDE.md), [docs/RUNBOOK.md](RUNBOOK.md) §7.

## TL;DR (если уже всё настроено)

```bash
./scripts/dev.sh up-fe                           # стартует все сервисы (port 80)
curl -s http://localhost:8012/health             # → Healthy
curl -s http://localhost:8012/health/ready       # → Healthy
docker logs -f --tail=50 assignment-review-service
```

Открыть `http://localhost/settings/integrations` → «Подключить ревью PR» (если уже стоит GitHub App в .env). Submit задачу с реальным PR → дёрнуть run-iteration → смотреть Grafana.

---

## Что нужно подготовить (owner-actions)

### 1. Dev GitHub App (отдельный от прод и от App #68)

Шаги в браузере (https://github.com/settings/apps/new):

| Поле | Значение для dev |
|---|---|
| **GitHub App name** | `Edu Platform Review (dev)` (уникально на github.com — добавь свой ник если занято) |
| **Homepage URL** | `http://localhost` |
| **Callback URL** | `http://localhost/api/assignment-review/installations/callback/` |
| **Request user authorization (OAuth) during installation** | ✅ ON |
| **Setup URL** | пусто |
| **Webhook → Active** | ✅ ON |
| **Webhook URL** | `https://<your-tunnel-host>/api/assignment-review/webhooks/github/` (см. §2) |
| **Webhook secret** | сгенерируй: `openssl rand -hex 32` — сохрани, понадобится для `.env` |

**Permissions** (минимум):

| Permission | Access |
|---|---|
| Repository → Pull requests | Read & write |
| Repository → Contents | Read-only |
| Repository → Metadata | Read-only (auto, обязательно) |

**Subscribe to events:**
- `installation`
- `installation_repositories`
- `installation_target` (опционально, если хочешь tracking re-install'ов)

После создания App'а:
1. Скопируй **App ID** (вверху страницы — `12345678`).
2. Скопируй **Slug** (из URL: `https://github.com/apps/<slug>`).
3. Скопируй **Client ID** (секция «About»).
4. На странице App'а — «Generate a private key» → скачается `.pem` файл. **Сохрани его** — повторно скачать нельзя.

### 2. Туннель для webhook'ов

GitHub шлёт webhook на публичный URL — `localhost` не достижим из интернета. Варианты:

**Cloudflare Tunnel (recommended — free, persistent URL):**

```bash
brew install cloudflared
cloudflared tunnel --url http://localhost:80
# → выдаст URL типа https://random-words-1234.trycloudflare.com
```

Скопируй URL → вставь в **Webhook URL** GitHub App'а: `https://random-words-1234.trycloudflare.com/api/assignment-review/webhooks/github/`.

**ngrok (free tier — URL меняется на каждый старт):**

```bash
brew install ngrok
ngrok http 80
# → https://abcd-1234.ngrok-free.app
```

⚠️ Туннель должен быть запущен **до** того, как GitHub App шлёт webhook (installation/uninstallation events).

### 3. Заполни `.env` (рядом с `.env.example`)

```bash
# В корне репозитория
cat <<'EOF' >> .env

# --- ARS dev GitHub App ---
ASSIGNMENT_REVIEW__GITHUB__APPID=12345678
ASSIGNMENT_REVIEW__GITHUB__SLUG=edu-platform-review-dev
ASSIGNMENT_REVIEW__GITHUB__CLIENTID=Iv23li...
ASSIGNMENT_REVIEW__GITHUB__WEBHOOKSECRET=<openssl rand -hex 32>

# Private key — конвертируй .pem в base64 одной строкой:
#   base64 -i ~/Downloads/edu-platform-review-dev.private-key.pem | tr -d '\n' | pbcopy
ASSIGNMENT_REVIEW__GITHUB__PRIVATEKEYPEMBASE64=LS0tLS1CRUdJTi...
EOF
```

`AI__PROVIDERS__AITUNNEL__APIKEY` уже должен быть в `.env` (используется `AssignmentReviewService`).

---

## Запуск

```bash
./scripts/dev.sh up-fe          # стартует всё, включая ARS на :8012
docker logs -f --tail=50 assignment-review-service
```

Признаки здорового старта:

```
Now listening on: http://[::]:8012
Application started.
[Wolverine] Application is starting up
[Healthcheck] AssignmentReviewService is healthy
```

Если в логах `[Wolverine] Connection to RabbitMQ failed` или `pgvector extension not found` — стопни и проверь `./scripts/dev.sh up-infra` отработал (postgres image `pgvector/pgvector:pg16`).

### Smoke check

```bash
# 1. Health endpoints
curl -s http://localhost:8012/health        # Healthy
curl -s http://localhost:8012/health/ready  # Healthy

# 2. OpenAPI / Scalar
open http://localhost:8012/scalar/v1

# 3. Через nginx (от фронта)
curl -s http://localhost/api/assignment-review/admin/ai-settings/ \
  -H "Cookie: <admin session cookie>"
# или с Bearer-токеном — см. /scalar для аутентификации
```

---

## End-to-end тест: реальный PR → AI-ревью

### 1. Создать тестовое задание с RAG-контекстом

В admin UI (или через MCP `edu_*` tools):

1. Создать **Project** с минимумом content. В detail-view проекта — поле **Review context** (markdown). Заполнить:
   ```markdown
   # Гайдлайны ревью
   - Все public методы должны иметь XML-doc.
   - Не использовать `Guid.NewGuid()` — только `Guid.CreateVersion7()`.
   - LINQ-методы без `Async` суффикса в async-handler'ах = ошибка.
   ```
   Это сохранится в `ProjectReviewContext.GuidelinesMarkdown`. Сразу publish'нётся event `project.review_context.updated` → ARS снимет snapshot.

2. (Опционально) **Ref-repo URL** на проект — указать существующий публичный репо (например, `https://github.com/sachkov/example-clean-arch`). ARS клонирует его и проиндексирует в RAG.

3. Создать **Issue** с типом `GitHubPullRequest` под этим проектом. В detail-view — **Review spec** (markdown):
   ```markdown
   # Задача: реализовать endpoint POST /orders/

   Что проверять:
   - Валидация request DTO через FluentValidation.
   - Repository вызывается через ITransactionManager.
   - Endpoint покрыт integration-тестом.
   ```
   Publish'нёт event `issue.review_spec.updated` → ARS снимет snapshot.

4. Привязать Issue к Module и Module к Course; Publish course.

### 2. Зачислиться студентом на курс

Авторизуйся в браузере как тестовый студент (`student@test.local` / создавай через AuthService OTP). Открой курс → Enroll. Если course `AccessType=FREE` — мгновенно. Если `ENROLLED` — нужен plan-grant (`/admin/access/grants`).

### 3. Установить ARS GitHub App в свой тестовый репо

1. От лица студента: `http://localhost/settings/integrations` → раздел **«Ревью PR»** → «Подключить GitHub-приложение».
2. Перебросит на `github.com/apps/<your-slug>/installations/new` — выбери репо (например, специально созданный `student/test-pr-repo`).
3. После approve — back на `/settings/integrations`, видишь карточку «✅ Подключено: 1 репозиторий».

### 4. Создать PR в репо

```bash
git clone git@github.com:student/test-pr-repo.git
cd test-pr-repo
git checkout -b feat/orders-endpoint
# ... добавить файлы ...
git commit -am "feat: add orders endpoint"
git push -u origin feat/orders-endpoint
# Открыть PR через GitHub UI или gh: gh pr create --fill
```

Запомни URL PR'а: `https://github.com/student/test-pr-repo/pull/1`.

### 5. Submit issue со ссылкой на PR

Студентом, на странице issue → форма submission → вставить PR URL → Submit.

В логах:
```
[ProgressService] IssueSubmission created, status=AWAITING_REVIEW
[ProgressService] Published issue_submission.awaiting_review → wolverine outbox
...
[AssignmentReviewService] IssueSubmissionAwaitingReviewHandler received submission_id=...
[AssignmentReviewService] AiReview created, status=QUEUED
[AssignmentReviewService] Published ai_review.queued_for_submission
...
[ProgressService] AiReviewQueuedForSubmission consumed → ReadyForHumanReview=false
```

### 6. Запустить iteration

Можно через UI (на странице submission — кнопка «AI-проверить»), но проще через curl от admin'а:

```bash
TOKEN="<admin Bearer token from /scalar>"
REVIEW_ID="<из логов ARS — или GET /reviews/by-submission/{submissionId}/>"

curl -X POST "http://localhost/api/assignment-review/reviews/$REVIEW_ID/run-iteration/" \
  -H "Authorization: Bearer $TOKEN"
```

Логи ARS (полный pipeline, см. `AssignmentReviewService/CLAUDE.md` §AI Review Pipeline):

```
[AssignmentReviewService] RunIterationHandler started review_id=...
[AssignmentReviewService] Loading PR + diff via IVcsProvider
[AssignmentReviewService] Diff: +127 lines, 3 files
[AssignmentReviewService] RAG retrieve: project=6 chunks, issue=5, author=3
[AssignmentReviewService] LLM call openai/gpt-4.1-mini, tokens_in=4231, tokens_out=823, latency=12.4s
[AssignmentReviewService] Parsed verdict=MINOR_ISSUES, inline_comments=4
[AssignmentReviewService] Posted PR review to github.com/student/test-pr-repo/pull/1
[AssignmentReviewService] AiReviewIterationCompleted published
```

На PR в GitHub появится комментарий от App'а (один summary + inline-комменты).

### 7. Проверить UI студента

Обнови страницу submission → блок «AI-ревью» с verdict, summary, кнопкой «Запросить ревью человека» (finalize). После finalize — submission видим в author inbox.

---

## Что мониторить (Grafana)

```bash
./scripts/dev.sh up-obs                          # поднимает Loki+Tempo+Prometheus+Grafana
open http://localhost:3001                       # admin/admin
```

Dashboard **`assignment-review-pipeline`** (8 панелей):

| Панель | Что проверять |
|---|---|
| Iteration duration p50/p95 | < 30s p95 на обычном PR (~100 lines) |
| Iteration outcomes | LOOKS_GOOD / MINOR_ISSUES / MAJOR_ISSUES / FAILED breakdown |
| Diff size distribution | детектит «слишком большие PR» (> 1500 → review.diff.too_large) |
| RAG chunks used | sanity check — должно быть 8-14 на обычный run |
| GitHub reviews posted | 1 per successful run (исключая LOOKS_GOOD без inline comments) |
| Indexing runs | если поднял ref-repo — 1+ run |
| AI request duration | latency LLM call'а — синтетик health |
| Error rate | < 1% на стабильных условиях |

Loki:
```
{service_name="AssignmentReviewService"} |= "error"
{service_name="AssignmentReviewService"} |= "RunIterationHandler"
```

Tempo: фильтр по `service.name=AssignmentReviewService` → span `iteration.run` → child span'ы `context.retrieve`, `llm.call`, `github.post_review`.

---

## Troubleshooting

| Симптом | Причина | Фикс |
|---|---|---|
| `review.no_installation` при run-iteration | Студент не подключил App к репо PR'а | `/settings/integrations` → подключить |
| `review.diff.too_large` | PR > 1500 added lines | Студенту разбить PR; либо bump `AssignmentReviewAI:Limits:MaxDiffAdditions` в `.env` (cost растёт линейно) |
| `vcs.repo.not_in_installation` | Репо PR'а не в whitelist installation'а | На GitHub App page → Repository access → Add repo |
| Webhook не приходит | Cloudflare tunnel упал / URL изменился | Перезапусти tunnel, обнови Webhook URL в GitHub App settings |
| `pgvector extension not found` при миграции | Не тот postgres image | `docker-compose.yml` должен использовать `pgvector/pgvector:pg16` — проверь |
| AiReview не создаётся после submit | `IssueSubmissionAwaitingReviewHandler` не отработал | `docker logs assignment-review-service \| grep IssueSubmissionAwaitingReviewHandler`; проверь `wolverine_dead_letters` в schema `assignment_review` |
| `403 review.access_denied` | Не owner submission'а и не admin | Подняться в `/scalar` под admin'ом или владельцем submission'а |
| LLM возвращает invalid JSON | Модель «гуляет» | Logged как `failure_reason=ai.invalid_json` — есть 1× retry внутри `RunIterationHandler`; если упорно — снизь `Temperature` в `appsettings.json` |

---

## Чек-лист перед prod-деплоем (для разблокировки #231)

- [ ] Создан **prod** GitHub App (отдельный от dev) с теми же permissions и events.
- [ ] Webhook URL у prod App'а: `https://<prod-domain>/api/assignment-review/webhooks/github/`.
- [ ] Все 5 `ASSIGNMENT_REVIEW__GITHUB__*` залиты в Infisical (prod env).
- [ ] Хотя бы один `ProjectReviewContext.GuidelinesMarkdown` + `IssueReviewSpec` создан на prod ECS.
- [ ] Ref-repo индексация прошла (метрика `assignment_review_indexing_runs_total{outcome="success"}` ≥ 1).
- [ ] Grafana dashboard `assignment-review-pipeline` доступен на prod Grafana.
- [ ] End-to-end smoke на staging или dev с production-симуляцией: реальный PR → submission → AiReview → inline comments в PR.

После выполнения — см. `backend/AssignmentReviewService/CLAUDE.md` § «Чтобы снять hold» для механики revert'а.
