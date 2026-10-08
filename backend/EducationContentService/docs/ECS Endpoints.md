# EducationContentService Endpoints

Документ описывает актуальные minimal API endpoint-ы сервиса по состоянию текущего кода в `EducationContentService.Core/Features`.

## 1. Public and learner-facing read endpoints

| Method | Path | Access | Назначение |
|---|---|---|---|
| `GET` | `/courses/catalog` | `AllowAnonymous` | Публичный каталог опубликованных курсов |
| `GET` | `/courses/{courseId}/landing` | `AllowAnonymous` | Лендинг курса с curriculum preview и stats |
| `GET` | `/courses/{courseId}/curriculum` | `AllowAnonymous` | Публичная структура опубликованного курса |
| `GET` | `/modules/{moduleId}/overview` | `AllowAnonymous` | Краткий обзор опубликованного модуля |
| `GET` | `/lessons/{lessonId}/detail` | `AllowAnonymous` | Detail урока; фактический доступ зависит от `AccessType` и entitlement |
| `GET` | `/issues/{issueId}/detail` | `AllowAnonymous` | Detail задачи; фактический доступ зависит от `AccessType` и entitlement |

## 2. Articles

| Method | Path | Access | Назначение |
|---|---|---|---|
| `GET` | `/articles` | `Content.VIEW` | Список статей с `scope=public|private|mine`, курсорная пагинация |
| `GET` | `/articles/{articleId}/detail` | `Content.VIEW` | Detail статьи с учётом public/private и course access |
| `POST` | `/articles` | `Articles.MANAGE` | Создать статью |
| `PATCH` | `/articles/{articleId}` | `Articles.MANAGE` | Обновить статью |
| `POST` | `/articles/{articleId}/publish` | `Articles.MANAGE` | Опубликовать статью |
| `POST` | `/articles/{articleId}/archive` | `Articles.MANAGE` | Архивировать статью |
| `POST` | `/articles/{articleId}/draft` | `Articles.MANAGE` | Вернуть статью в draft |

## 3. Courses

| Method | Path | Access | Назначение |
|---|---|---|---|
| `POST` | `/courses` | `Courses.MANAGE` | Создать курс |
| `GET` | `/courses/my` | `Courses.MANAGE` | Список собственных курсов автора |
| `GET` | `/courses/{courseId}/detail` | `Courses.MANAGE` | Management detail курса |
| `GET` | `/courses/{courseId}/builder` | `Courses.MANAGE` | Builder read-model курса |
| `GET` | `/courses/{courseId}/lessons` | `Courses.MANAGE` | Список уроков курса для authoring |
| `PATCH` | `/courses/{courseId}` | `Courses.MANAGE` | Обновить курс |
| `POST` | `/courses/{courseId}/publish` | `Courses.MANAGE` | Опубликовать курс |
| `POST` | `/courses/{courseId}/archive` | `Courses.MANAGE` | Архивировать курс |
| `POST` | `/courses/{courseId}/restore` | `Courses.MANAGE` | Восстановить курс |
| `DELETE` | `/courses/{courseId}` | `Courses.MANAGE` | Удалить курс |
| `GET` | `/courses/github-orgs/{slug}/validate` | `Courses.MANAGE` | Проверить GitHub org slug |

## 4. Course items

| Method | Path | Access | Назначение |
|---|---|---|---|
| `POST` | `/courses/{courseId}/modules` | `Courses.MANAGE` | Создать модуль в курсе |
| `POST` | `/courses/{courseId}/projects` | `Courses.MANAGE` | Создать проект в курсе |
| `PATCH` | `/courses/{courseId}/items/{referenceId}/move` | `Courses.MANAGE` | Переместить section верхнего уровня |
| `DELETE` | `/courses/{courseId}/items/{referenceId}` | `Courses.MANAGE` | Отвязать module/project от курса |

## 5. Course articles

| Method | Path | Access | Назначение |
|---|---|---|---|
| `GET` | `/courses/{courseId}/articles` | `Content.VIEW` | Список course-level статей |
| `POST` | `/courses/{courseId}/articles` | `Courses.MANAGE` | Прикрепить статью к курсу |
| `PATCH` | `/courses/{courseId}/articles/{articleId}/move` | `Courses.MANAGE` | Переместить course article |
| `DELETE` | `/courses/{courseId}/articles/{articleId}` | `Courses.MANAGE` | Удалить статью из course materials |

## 6. Modules and module items

| Method | Path | Access | Назначение |
|---|---|---|---|
| `GET` | `/modules/{moduleId}/detail` | `Courses.MANAGE` | Management detail модуля |
| `PATCH` | `/modules/{moduleId}` | `Modules.MANAGE` | Обновить модуль |
| `POST` | `/modules/{moduleId}/publish` | `Modules.MANAGE` | Опубликовать модуль |
| `POST` | `/modules/{moduleId}/archive` | `Modules.MANAGE` | Архивировать модуль |
| `POST` | `/modules/{moduleId}/restore` | `Modules.MANAGE` | Восстановить модуль |
| `POST` | `/modules/{moduleId}/lessons` | `Lessons.MANAGE` | Создать урок в модуле |
| `POST` | `/modules/{moduleId}/articles` | `Modules.MANAGE` | Прикрепить статью к модулю |
| `POST` | `/modules/{moduleId}/issues/{issueId}` | `Modules.MANAGE` | Прикрепить issue к модулю |
| `PATCH` | `/modules/{moduleId}/items/{referenceId}/move` | `Modules.MANAGE` | Изменить порядок module item |
| `PATCH` | `/modules/{moduleId}/items/{referenceId}/priority` | `Modules.MANAGE` | Обновить `ViewPriority` |
| `PATCH` | `/modules/{sourceModuleId}/items/{referenceId}/transfer` | `Modules.MANAGE` | Перенести item между модулями |
| `DELETE` | `/modules/{moduleId}/items/{referenceId}` | `Modules.MANAGE` | Отвязать item от модуля |

## 7. Lessons

| Method | Path | Access | Назначение |
|---|---|---|---|
| `PATCH` | `/lessons/{lessonId}` | `Lessons.MANAGE` | Обновить урок |
| `POST` | `/lessons/{lessonId}/publish` | `Lessons.MANAGE` | Опубликовать урок |
| `POST` | `/lessons/{lessonId}/archive` | `Lessons.MANAGE` | Архивировать урок |
| `POST` | `/lessons/{lessonId}/restore` | `Lessons.MANAGE` | Восстановить урок |

## 8. Projects and issues

| Method | Path | Access | Назначение |
|---|---|---|---|
| `GET` | `/projects/{projectId}/detail` | `Courses.MANAGE` | Management detail проекта |
| `PATCH` | `/projects/{projectId}` | `Issues.MANAGE` | Обновить проект |
| `POST` | `/projects/{projectId}/publish` | `Issues.MANAGE` | Опубликовать проект |
| `POST` | `/projects/{projectId}/archive` | `Issues.MANAGE` | Архивировать проект |
| `POST` | `/projects/{projectId}/restore` | `Issues.MANAGE` | Восстановить проект |
| `POST` | `/projects/{projectId}/issues` | `Issues.MANAGE` | Создать issue в проекте |
| `PATCH` | `/projects/{projectId}/issues/{issueId}/move` | `Issues.MANAGE` | Переместить issue в проекте |
| `DELETE` | `/projects/{projectId}/issues/{issueId}` | `Issues.MANAGE` | Отвязать issue от проекта |
| `PATCH` | `/issues/{issueId}` | `Issues.MANAGE` | Обновить issue |
| `PUT` | `/issues/{issueId}/external-links` | `Issues.MANAGE` | Обновить внешние ссылки issue |
| `PUT` | `/issues/{issueId}/internal-materials` | `Issues.MANAGE` | Обновить внутренние материалы issue |
| `POST` | `/issues/{issueId}/publish` | `Issues.MANAGE` | Опубликовать issue |
| `POST` | `/issues/{issueId}/archive` | `Issues.MANAGE` | Архивировать issue |
| `POST` | `/issues/{issueId}/restore` | `Issues.MANAGE` | Восстановить issue |

## 9. Internal progress lookup endpoints

Все endpoint-ы ниже требуют `PlatformRoles.SERVICE` или `PlatformRoles.ADMIN`.

| Method | Path | Назначение |
|---|---|---|
| `GET` | `/internal/progress/courses/{courseId}` | Метаданные курса для progress/integration |
| `GET` | `/internal/progress/courses/{courseId}/modules/{moduleId}` | Метаданные модуля |
| `GET` | `/internal/progress/modules/{moduleId}/lessons/{lessonId}` | Метаданные урока |
| `GET` | `/internal/progress/courses/{courseId}/projects/{projectId}` | Метаданные проекта |
| `GET` | `/internal/progress/projects/{projectId}/issues/{issueId}` | Метаданные issue |
| `POST` | `/internal/progress/courses/blueprints` | Батч blueprint-ов курса для расчёта totals |
| `POST` | `/internal/progress/courses/by-github-org` | Найти курсы по GitHub org slug |
| `GET` | `/internal/progress/courses/github-org-slugs` | Список org slug-ов для sync |
| `POST` | `/internal/progress/materials/resolve` | Resolve material target -> section/course titles |

## 10. Endpoint notes

### 10.1 Articles

- `GET /articles` требует `Content.VIEW`, а не `Articles.MANAGE`;
- `scope=public` для обычного пользователя возвращает только `is_public = true` и `status = Published`;
- author/manager видит свои публичные статьи шире, чем обычный потребитель.

### 10.2 Course visibility

- `landing` и `curriculum` возвращают только опубликованный курс;
- приватные статьи внутри модуля на этих endpoint-ах скрываются, если у пользователя нет доступа к курсу;
- анонимный запрос не увидит приватные article items.

### 10.3 Lesson and issue detail

- endpoint-ы `AllowAnonymous`, но реальный доступ вычисляется внутри handler;
- `FREE` material доступен без логина;
- `ENROLLED` material требует entitlement;
- для неавторизованного пользователя возвращается `UnauthorizedAccess`, для авторизованного без права доступа `AccessDenied`.

### 10.4 Quiz

Отдельных quiz endpoint-ов на текущий момент нет, несмотря на наличие `Quiz` в домене и БД.
