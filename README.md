# Education Platform

Образовательная платформа (LMS) для управления курсами и учебным контентом.

## Стек технологий

| Слой           | Технологии                                                                        |
| -------------- | --------------------------------------------------------------------------------- |
| Backend        | .NET 10.0, Clean Architecture + DDD, Wolverine + RabbitMQ, EF Core 10, PostgreSQL |
| Frontend       | Next.js 16, React 19, TypeScript, Tailwind CSS 4, shadcn/ui                       |
| Инфраструктура | Docker Compose, Nginx, MinIO/Yandex S3, Redis, Mailpit                            |
| Observability  | OpenTelemetry, Tempo, Loki, Prometheus, Grafana                                   |

## Быстрый старт

### Требования

- Docker и Docker Compose
- .NET 10 SDK (для локальной разработки backend)
- Node.js 22+ (для локальной разработки frontend)

### 1. Настройка окружения

```bash
# Скопировать env-файл и заполнить значения
cp .env.example .env

# Настроить публичный NuGet feed
cp backend/nuget.config.example backend/nuget.config
```

### 2. Запуск

**Вариант A — полностью в Docker (рекомендуется для первого запуска):**

```bash
./scripts/dev.sh up-fe    # infra + backend + frontend в Docker
# Открыть http://localhost/
```

**Вариант B — фронтенд локально (для разработки):**

```bash
./scripts/dev.sh up       # infra + backend + nginx
./scripts/dev.sh gen-fe-env  # генерирует frontend/.env.local из .env
cd frontend && npm install && npm run dev
# Открыть http://localhost/ (через nginx, НЕ :3000)
```

### 3. Заполнение данными (опционально)

```bash
# Применить seed-данные (курсы, материалы, теги)
./scripts/dev.sh seed --author-id <guid-вашего-пользователя>
```

## Сервисы

| Сервис                  | Порт | Схема         | Назначение                                                |
| ----------------------- | ---- | ------------- | --------------------------------------------------------- |
| EducationContentService | 8001 | education     | Курсы, модули, материалы, проекты, задачи, учебный план   |
| FileService             | 8002 | files         | Файловое хранилище (S3/MinIO), видео (Kinescope)          |
| ProgressService         | 8003 | progress      | Записи на курс, прогресс, ревью задач                     |
| CommentService          | 8004 | comments      | Комментарии с курсор-пагинацией                           |
| AuthService             | 8005 | auth          | OpenIddict OIDC, Identity, профили, автор-пространства    |
| NotificationService     | 8006 | notifications | In-app inbox, SSE-стрим, pluggable delivery channels      |
| TelegramBotService      | 8008 | —             | Telegram-бот (DM-инвайты, chat-bindings, F1/F6 flows)     |

Все сервисы используют одну БД `education_platform` с изоляцией по схемам.

## Dev команды

```bash
./scripts/dev.sh up          # infra + backend + nginx
./scripts/dev.sh up-fe       # + frontend в Docker
./scripts/dev.sh up-all      # полный стек с observability
./scripts/dev.sh up-infra    # только инфраструктура
./scripts/dev.sh up-obs      # infra + app + observability
./scripts/dev.sh migrate     # прогнать контейнеры миграций
./scripts/dev.sh gen-fe-env  # сгенерировать frontend/.env.local
./scripts/dev.sh export-seed # экспорт данных в seed-data.sql
./scripts/dev.sh seed        # применить seed-data.sql
./scripts/dev.sh backup      # pg_dumpall бэкап (gzip, ротация 10)
./scripts/dev.sh down        # остановить всё
```

## Доступ

| Сервис            | URL                   |
| ----------------- | --------------------- |
| Приложение        | http://localhost       |
| Mailpit (письма)  | http://localhost:8025  |
| RabbitMQ UI       | http://localhost:15672 |
| MinIO UI          | http://localhost:9001  |
| Grafana (obs)     | http://localhost:3001  |

> **Важно:** фронтенд всегда открывать через `http://localhost/` (nginx), а не напрямую через `:3000`. Nginx проксирует API и обрабатывает CORS.

## Структура проекта

```text
education-platform/
├── backend/
│   ├── AuthService/                 # OIDC, Identity, автор-пространства
│   ├── EducationContentService/     # Курсы, модули, материалы, задачи
│   ├── FileService/                 # Файлы (S3), видео (Kinescope)
│   ├── ProgressService/             # Прогресс, записи на курс, ревью
│   ├── CommentService/              # Комментарии
│   ├── NotificationService/         # In-app inbox + SSE
│   ├── TelegramBotService/          # Telegram-бот
│   └── Shared/                      # Общие пакеты (Auth, ContentAccess, Core)
├── frontend/                        # Next.js (Feature-Sliced Design)
├── docker/                          # Конфиги инфраструктуры
├── scripts/                         # dev.sh, backup-s3.sh
├── docker-compose.yml               # Dev compose
├── docker-compose.prod.yml          # Production compose
├── nginx.conf / nginx.prod.conf     # Reverse proxy
├── CLAUDE.md                        # Инструкции для AI-агентов
└── AGENTS.md                        # Универсальный справочник по архитектуре
```

## Разработка

### Миграции

```bash
# Создать новую миграцию
dotnet ef migrations add {Name} \
  --project backend/{Service}/{Service}.Infrastructure.Postgres \
  --startup-project backend/{Service}/{Service}.Web
```

> Миграции **иммутабельны** — нельзя удалять или изменять существующие файлы миграций. Для исправления — создать новую миграцию.

### Тесты

```bash
# Все тесты (нужен запущенный Docker для Testcontainers)
cd backend && dotnet test backend.slnx

# Конкретный сервис
dotnet test backend/FileService/tests/FileService.IntegrationTests

# Frontend
cd frontend && npm test
```

### Сборка

```bash
cd backend && dotnet build backend.slnx   # backend
cd frontend && npm run build              # frontend
```

## Конвенции

- **Enum casing:** все строковые enum-значения в БД — `UPPER_CASE` (`DRAFT`, `PUBLISHED`, `ENROLLED`)
- **API URLs:** всегда с trailing slash (`/api/courses/`, не `/api/courses`)
- **UUID:** `Guid.CreateVersion7()` в коде, `Guid.NewGuid()` — только в тестах
- **Commits:** conventional commits (`feat:`, `fix:`, `chore:`, `refactor:`)

## Лицензии и публичные исходники

Авторский код, документация разработчика и демонстрационные данные доступны под [MIT](LICENSE).
[Сторонние компоненты](THIRD_PARTY_LICENSES/README.md) сохраняют свои лицензии.
[Продуктовые assets](ASSET_NOTICE.md) имеют отдельные права.
[Граница публичного дерева](docs/public-source.md) описывает примеры данных и конфигурацию.

## Документация

- [CLAUDE.md](CLAUDE.md) — полный справочник для AI-агентов (инструкции, команды, архитектура)
- [AGENTS.md](AGENTS.md) — универсальный справочник по доменной модели и коммуникации сервисов
- [CHANGELOG.md](CHANGELOG.md) — история изменений
