# Архитектура EducationContentService

## 1. Назначение сервиса

`EducationContentService` отвечает за структуру и метаданные учебного контента платформы:

- курсы;
- модули;
- уроки;
- проекты;
- задачи;
- статьи;
- связи между этими сущностями;
- внутренние lookup-endpoint-ы для `ProgressService`;
- синхронизацию части access-метаданных в Redis.

Сервис не хранит прогресс, комментарии, профили, файлы и видео. Эти домены вынесены в другие сервисы.

## 2. Текущая модель предметной области

Иерархия контента сейчас выглядит так:

```text
Course
├── CourseItem(Module)
│   └── Module
│       └── ModuleItem
│           ├── Lesson
│           ├── Article
│           ├── Issue
│           └── Quiz (доменная сущность есть, HTTP/API-слайсов пока нет)
├── CourseItem(Project)
│   └── Project
│       └── ProjectItem
│           └── Issue
└── CourseArticle
    └── Article
```

Ключевая особенность текущей реализации:

- статья может жить как глобальная сущность `Article`;
- статья может быть прикреплена к модулю через `ModuleItemType.Article`;
- статья может быть прикреплена к курсу как отдельный материал через `CourseArticle`;
- приватные статьи фильтруются в публичных read-model в зависимости от прав и entitlement к курсу.

## 3. Слои приложения

Проект разбит на стандартные для сервиса слои:

- `EducationContentService.Web`
  runtime-host, middleware pipeline, OpenAPI/Scalar, health checks, wiring;
- `EducationContentService.Core`
  vertical slices, validators, handlers, integration logic, internal queries;
- `EducationContentService.Domain`
  aggregate roots, value objects, инварианты, enum-ы;
- `EducationContentService.Infrastructure.Postgres`
  `DbContext`, EF-конфигурации, миграции, репозитории, транзакции, outbox;
- `EducationContentService.Contracts`
  DTO и HTTP-клиент для межсервисного общения.

## 4. Архитектурный стиль

### 4.1 Vertical Slice + CQRS

Каждый сценарий обычно лежит в одном файле или небольшой группе файлов:

- `Command` / `Query`;
- `Validator`;
- `Endpoint`;
- `Handler`.

Запись и бизнес-инварианты проходят через домен и EF Core. Чтение построено в основном на Dapper-запросах поверх `DbConnection`, которую отдаёт `ITransactionManager`.

### 4.2 Разделение read/write моделей

Write-side:

- репозитории `I*Repository`;
- агрегаты и value objects;
- `EducationDbContext`;
- unit-of-work через `ITransactionManager`.

Read-side:

- Dapper;
- отдельные SQL под конкретные DTO;
- агрегация данных из `courses`, `modules`, `module_items`, `projects`, `project_items`, `articles`, `issues`, `lessons`.

### 4.3 Ordering

Порядок разделов курса, элементов модуля, элементов проекта и course articles строится на `SortKey` из `Shared/Ordering`.

Это даёт:

- стабильный порядок без массового пересчёта индексов;
- перемещение `before/after`;
- дешёвые reorder-операции в UI course builder.

## 5. Хранилище данных

Сервис использует схему PostgreSQL `education`.

Основные таблицы:

- `courses`
- `course_items`
- `course_articles`
- `modules`
- `module_items`
- `lessons`
- `projects`
- `project_items`
- `issues`
- `articles`
- `quizzes`
- wolverine/outbox storage в той же схеме

Состояние схемы определяется EF Core migrations из `EducationContentService.Infrastructure.Postgres/Migrations`.

Актуальные важные изменения схемы:

- `course_articles` для библиотеки материалов курса;
- `articles.is_public` для разделения публичных и приватных статей;
- `modules.description` сделан optional;
- `modules` и `projects` поддерживают `detailed_description`;
- `module_items` имеют `view_priority`;
- `courses` поддерживают `slug`;
- добавлены performance indexes.

## 6. Контроль доступа

### 6.1 Permission-based API access

Management-endpoint-ы закрываются через `RequirePermissions(...)`.

Основные группы прав:

- `Courses.MANAGE`
- `Modules.MANAGE`
- `Lessons.MANAGE`
- `Issues.MANAGE`
- `Articles.MANAGE`
- `Content.VIEW`

Internal integration-endpoint-ы для `ProgressService` закрываются через `RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN)`.

### 6.2 Runtime access к учебным материалам

Для lesson и issue используется `IEntitlementChecker` из shared content-access слоя.

Текущая логика:

- `Lesson.AccessType = FREE` открывает урок анонимно;
- `Lesson.AccessType = ENROLLED` требует entitlement к ресурсу `lesson`;
- `Issue.AccessType = FREE` открывает задачу анонимно;
- `Issue.AccessType = ENROLLED` требует entitlement к ресурсу `issue`;
- приватные статьи не читаются анонимно;
- приватные статьи доступны:
  - менеджерам контента;
  - автору статьи;
  - пользователю с entitlement к опубликованному курсу, где статья прикреплена к курсу или модулю.

### 6.3 Redis content-access sync

Сервис синхронизирует access tags для уроков в Redis:

- `lesson.created`
- `lesson.access_type_changed`
- `lesson.hard_deleted`

Если урок `ENROLLED` и не привязан ни к одному курсу, ему ставится sentinel tag `enrolled:unassigned`, чтобы доступ был гарантированно закрыт.

## 7. Интеграции

### 7.1 FileService

Сервис не хранит бинарные файлы сам.

Через `IFileServiceClient` он:

- получает metadata по image/video;
- делает batch-resolve файлов и видео;
- bind/sync draft assets;
- реагирует на file events.

Над клиентом навешан `CachedFileServiceClient`:

- HybridCache;
- L2 TTL: 5 минут;
- local TTL: 1 минута;
- кешируются `GetFile`, `GetVideo`, `GetFilesBatch`, `GetVideosBatch`;
- `BindDraftAssets` и `SyncEntityAssets` идут напрямую без кеша.

### 7.2 GitHub API

Используется отдельный `HttpClient("GitHubApi")`:

- `BaseAddress = https://api.github.com/`;
- retry `3x`;
- circuit breaker `5 failures / 30s`;
- нужен для валидации `GitHubOrgSlug` и интеграций прогресса.

### 7.3 ProgressService

`EducationContentService` отдаёт internal lookup API для:

- метаданных курса, модуля, урока, проекта, задачи;
- blueprint-ов курса для расчёта прогресса;
- resolve учебных материалов в пределах курса;
- GitHub org sync.

## 8. Messaging

Используется Wolverine + RabbitMQ + PostgreSQL durable inbox/outbox.

### 8.1 Outgoing education events

Сейчас публикуются:

- `LessonAccessTypeChanged`
- `LessonCreated`
- `LessonSoftDeleted`
- `LessonRestored`
- `LessonHardDeleted`
- `IssueCreated`
- `IssueSoftDeleted`
- `IssueRestored`
- `IssueHardDeleted`
- `ModuleSoftDeleted`
- `ModuleHardDeleted`
- `CourseHardDeleted`
- `EntityHardDeleted`
- `FileAssetDetached`

### 8.2 Incoming file events

Сервис слушает две очереди:

- `education_content.file.lesson_events`
- `education_content.file.course_events`

Из них обрабатываются события привязки/удаления preview/video и инициации загрузки lesson video.

### 8.3 Durable messaging

Wolverine настроен так:

- postgres persistence в схеме `education`;
- durable inbox на всех listener-ах;
- durable outbox на всех sending endpoint-ах;
- auto-create/update storage вне production.

## 9. HTTP surface

### 9.1 Public/read API

Анонимно доступны:

- `GET /courses/catalog`
- `GET /courses/{courseId}/landing`
- `GET /courses/{courseId}/curriculum`
- `GET /modules/{moduleId}/overview`
- `GET /lessons/{lessonId}/detail`
- `GET /issues/{issueId}/detail`

Но фактическая доступность содержимого внутри detail endpoint-ов зависит от entitlement.

### 9.2 Authoring / management API

Через management API сейчас реально поддерживаются:

- CRUD-ish сценарии для курсов;
- создание и reorder модулей и проектов внутри курса;
- создание и управление уроками внутри модуля;
- создание и управление issue внутри проекта;
- attach issue/article к модулю;
- attach article к курсу;
- reorder/detach/transfer items;
- article library (`articles`, `courses/{id}/articles`).

### 9.3 Internal service API

Есть отдельная группа internal endpoint-ов под `ProgressService`:

- `internal/progress/courses/*`
- `internal/progress/modules/*`
- `internal/progress/projects/*`
- `internal/progress/materials/resolve`

## 10. Что важно помнить при изменениях

- `Quiz` уже есть в домене и persistence, но полноценного HTTP/API слоя для него сейчас нет;
- приватные статьи влияют на curriculum, landing, module overview и article detail, поэтому любые изменения article-access нужно проверять во всех этих read-model;
- при добавлении нового метода в `IFileServiceClient` нужно обновлять `CachedFileServiceClient`;
- ordering всех join-сущностей завязан на `SortKey`, не надо подменять это ручными integer-позициями;
- любые новые события, влияющие на доступ к урокам, нужно синхронизировать с Redis content-access слоем;
- internal endpoint-ы ECS являются контрактом для `ProgressService`, менять их нужно вместе с `Contracts` и consumer-ами.

## 11. Текущее состояние относительно старых V2-планов

Сервис уже не является просто планом или target-архитектурой:

- articles реализованы;
- course articles реализованы;
- progress lookup реализован;
- file event integration реализована;
- Redis content access для уроков реализован;
- quiz остаётся частично реализованным только на уровне домена/БД.

Поэтому документацию ниже нужно читать как описание текущего production-like состояния, а не как RFC.
