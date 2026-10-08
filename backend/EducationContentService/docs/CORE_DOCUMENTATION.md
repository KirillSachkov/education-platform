# EducationContentService Core

## 1. Структура Core-слоя

`EducationContentService.Core` содержит application-layer логику и vertical slices.

Текущие feature-группы:

- `Articles`
- `ContentAccess`
- `CourseArticles`
- `CourseItems`
- `Courses`
- `FileEvents`
- `ModuleItems`
- `ProgressLookup`
- `ProjectItems`

Внутри каждой группы лежат:

- команды и запросы;
- validators;
- minimal API endpoints;
- handlers;
- service-классы для join/create логики;
- repository abstractions при необходимости.

## 2. Dependency injection

### 2.1 `AddCore(...)`

Регистрирует:

- `CourseItemService`
- `ModuleItemService`
- `ProjectItemService`
- handlers из assembly
- validators из assembly
- `IFileServiceClient` через HTTP communication
- `HybridCache`
- `CachedFileServiceClient` как decorator
- `MemoryCache`
- `HttpClient("GitHubApi")`

### 2.2 `AddInfrastructurePostgres(...)`

Регистрирует:

- все repository implementation;
- `CourseArticleService`;
- `EducationDbContext`;
- `ITransactionManager`;
- `IOutboxService`;
- Wolverine `IDbContextOutbox<EducationDbContext>`;
- domain events.

## 3. Web/runtime composition

`EducationContentService.Web` поднимает:

- Serilog;
- OpenAPI;
- Scalar;
- JWT auth;
- CORS;
- request correlation;
- health checks;
- Wolverine;
- automatic endpoint discovery.

Pipeline:

1. forwarded headers
2. CORS
3. exception middleware
4. request correlation id
5. JWT authentication
6. request logging
7. openapi/scalar/health
8. mapped endpoints

## 4. Persistence and reads

### 4.1 EF Core

Используется для:

- загрузки агрегатов;
- записи и транзакций;
- миграций;
- domain events / outbox integration.

`EducationDbContext` содержит наборы:

- `Articles`
- `Courses`
- `CourseArticles`
- `CourseItems`
- `Lessons`
- `Modules`
- `ModuleItems`
- `Projects`
- `ProjectItems`
- `Issues`
- `Quizzes`

### 4.2 Dapper

Используется для read-model и integration lookup.

Наиболее насыщенные Dapper-query сейчас:

- `GetCatalog`
- `GetCourseBuilder`
- `GetCourseLanding`
- `GetCurriculum`
- `GetCourseLessons`
- `GetArticles`
- `GetCourseArticles`
- `GetModuleDetail`
- `GetModuleOverview`
- `GetLessonDetail`
- `GetProjectDetail`
- `GetIssueDetail`
- весь `ProgressLookup`

Причина такого разделения:

- сложные join-ы;
- дешёвые projection DTO;
- отсутствие лишнего EF tracking;
- удобная пакетная агрегация read-model.

## 5. Feature groups

## 5.1 Articles

Покрывает:

- список статей `GET /articles`;
- detail статьи `GET /articles/{id}/detail`;
- `POST /articles`;
- `PATCH /articles/{id}`;
- `POST /articles/{id}/publish`;
- `POST /articles/{id}/archive`;
- `POST /articles/{id}/draft`.

Особенности:

- список поддерживает `scope=public|private|mine`;
- для author/manager выдача публичных статей шире, чем для обычного пользователя;
- detail статьи учитывает entitlement к курсам, где статья используется.

## 5.2 CourseArticles

Отдельная feature-группа для course-level article library.

Покрывает:

- `GET /courses/{courseId}/articles`
- `POST /courses/{courseId}/articles`
- `DELETE /courses/{courseId}/articles/{articleId}`
- `PATCH /courses/{courseId}/articles/{articleId}/move`

Особенности:

- course article не смешан с `CourseItem`;
- attach не допускает дублей;
- read access для обычного пользователя требует access к курсу;
- manager видит все статьи, обычный пользователь только опубликованные.

## 5.3 Courses

Покрывает:

- catalog / landing / curriculum / builder / detail / lessons / my courses;
- create / update / publish / archive / restore / delete;
- validate GitHub org.

Особенности:

- `landing` и `curriculum` анонимные, но фильтруют приватные статьи;
- `builder` отдаёт полную management read-model, включая unpublished section/items;
- `catalog` кэшируется через HybridCache;
- `landing` и `curriculum` имеют user-aware cache keys из-за article visibility.

## 5.4 CourseItems

Покрывает верхний уровень курса:

- create module;
- create project;
- detach item;
- move item.

## 5.5 ModuleItems

Покрывает:

- detail/overview модуля;
- detail урока;
- create/update/publish/archive/restore lesson;
- update/publish/archive/restore module;
- attach article to module;
- attach issue to module;
- detach item;
- move item;
- transfer item;
- update view priority.

Особенности:

- `ModuleOverview` анонимный, но скрывает приватные article items без course access;
- `LessonDetail` проверяет entitlement к ресурсу `lesson`;
- `TransferModuleItem` важен для drag-and-drop сценариев между модулями.

## 5.6 ProjectItems

Покрывает:

- project detail;
- issue detail;
- create/update/publish/archive/restore issue;
- update issue external links;
- update issue internal materials;
- create/update/publish/archive/restore project;
- move/detach issue внутри проекта.

Особенности:

- `IssueDetail` анонимный только для `FREE` issue;
- internal materials enrich-ятся названиями и thumbnail-ами уроков через FileService.

## 5.7 ProgressLookup

Internal surface для `ProgressService`.

Покрывает:

- `GetCourseLookup`
- `GetModuleLookup`
- `GetLessonLookup`
- `GetProjectLookup`
- `GetIssueLookup`
- `GetCourseProgressBlueprints`
- `GetCoursesByGithubOrg`
- `GetGithubSyncOrgSlugs`
- `ResolveMaterialTargets`

Особенности:

- доступ только `SERVICE`/`ADMIN`;
- это контракт между сервисами, а не UI API;
- несколько endpoint-ов используют батчевые запросы и deduplication входных данных.

## 5.8 FileEvents

Покрывает реакцию ECS на события из `FileService`:

- привязка preview/video к lesson;
- удаление preview/video у lesson;
- привязка preview/video к course;
- удаление preview/video у course;
- upload initiated для lesson video;
- cache eviction по file asset change.

## 5.9 ContentAccess

Покрывает синхронизацию lesson access tags в Redis:

- `SyncLessonAccessOnCreationHandler`
- `SyncLessonAccessToRedisHandler`
- `ClearLessonAccessOnDeletionHandler`

## 6. Messaging

Настройка вынесена в:

- `Core/Messaging/WolverineConfiguration.cs`
- `Core/Messaging/RabbitMqConfiguration.cs`

Что важно:

- RabbitMQ exchanges `education.events` и `file.events`;
- quorum queues;
- durable inbox/outbox;
- отдельная очередь `education_content.content_access.sync` для событий, влияющих на доступ к урокам.

## 7. Caching

### 7.1 File metadata cache

`CachedFileServiceClient` кэширует:

- file metadata;
- video metadata;
- batch results.

### 7.2 Read-model cache

HybridCache сейчас используется, например, в:

- `GetCatalog`
- `GetCourseLanding`
- `GetCurriculum`

Нюанс:

- `GetCourseLanding` и `GetCurriculum` не могут иметь один глобальный cache key на курс, потому что приватные статьи зависят от текущего пользователя и его прав.

## 8. Validation

Валидация строится через FluentValidation и shared error helpers.

Типовые проверки:

- non-empty `Guid`;
- длины текста;
- допустимость enum/string значений;
- лимиты на size батчевых запросов;
- валидность value object через `MustBeValueObject(...)`.

## 9. Integration patterns

### 9.1 FileService integration

Слой `Core` зависит от `FileService.Contracts.HttpCommunication`.

Использование:

- image/video resolve для course catalog, landing, lesson detail, issue detail, progress blueprints;
- bind/sync assets в write-сценариях;
- side effects по file events.

### 9.2 GitHub integration

Слой `Core` валидирует GitHub org slug и поддерживает progress-related org lookup.

### 9.3 Platform auth

Все permission checks и service-role checks сделаны на уровне endpoint mapping, а runtime-user context приходит через `UserScopedData`.

## 10. Точки риска при изменениях

- если меняется visibility article, нужно проверить `GetArticleDetail`, `GetCourseArticles`, `GetCourseLanding`, `GetCurriculum`, `GetModuleOverview`;
- если меняется структура curriculum, нужно синхронно обновлять `Contracts.Courses` и frontend/course-learning;
- если меняется `ProgressLookup`, нужно синхронно обновлять `ProgressService`;
- если меняется file integration, нужно проверить decorator `CachedFileServiceClient` и event handlers;
- если меняется lesson access, нужно проверить Redis sync handlers и entitlement-based detail endpoints.
