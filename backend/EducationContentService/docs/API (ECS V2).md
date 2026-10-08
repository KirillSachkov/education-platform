# API EducationContentService

Этот документ описывает актуальный API-контракт ECS, а не старую целевую V2-схему.

## 1. Общие принципы

- формат API: minimal API + JSON;
- management surface закрыт permission-ами;
- часть read endpoint-ов анонимная, но runtime-access всё равно может быть ограничен;
- internal integration surface отделена префиксом `/internal/progress/...`;
- список DTO берётся из `EducationContentService.Contracts`.

## 2. Основные API-группы

### 2.1 Courses

Основные DTO:

- `CreateCourseRequest`
- `UpdateCourseRequest`
- `CourseCatalogDto`
- `CourseSummaryDto`
- `CourseDetailDto`
- `CourseBuilderDto`
- `CourseLandingDto`
- `CourseCurriculumDto`
- `CourseLessonDto`

Ключевые поля курса:

- `title`
- `description`
- `price`
- `gitHubOrgSlug`
- `detailedDescription`
- `learningOutcomes`
- `prerequisites`
- `imageId`
- `videoId`
- `slug`

### 2.2 Modules

Основные DTO:

- `CreateCourseModuleRequest`
- `UpdateModuleRequest`
- `ModuleDetailDto`
- `ModuleOverviewDto`
- `MoveModuleItemRequest`
- `TransferModuleItemRequest`
- `UpdateModuleItemViewPriorityRequest`
- `AttachModuleArticleRequest`

### 2.3 Lessons

Основные DTO:

- `CreateModuleLessonRequest`
- `UpdateLessonRequest`
- `LessonDetailDto` из core query

Урок содержит:

- `title`
- `content`
- `accessType`
- `imageId`
- `videoId`
- runtime-resolved `video` metadata

### 2.4 Projects and issues

Основные DTO:

- `UpdateProjectRequest`
- `ProjectDetailDto`
- `CreateProjectIssueRequest`
- `UpdateIssueRequest`
- `UpdateIssueExternalLinksRequest`
- `UpdateIssueInternalMaterialsRequest`
- `IssueDetailDto`

Issue поддерживает:

- markdown body;
- `accessType`;
- internal materials;
- external links.

### 2.5 Articles

Основные DTO:

- `CreateArticleRequest`
- `UpdateArticleRequest`
- `ArticleSummaryDto`
- `ArticleDetailDto`
- `CourseArticleSummaryDto`
- `AttachCourseArticleRequest`
- `MoveCourseArticleRequest`

У статьи два измерения видимости:

- `status`
- `isPublic`

Это принципиально: опубликованная приватная статья существует, но не является публично читаемой без author/manage/course entitlement.

### 2.6 Progress lookup

Основные DTO:

- `CourseDto`
- `ModuleDto`
- `LessonDto`
- `ProjectDto`
- `IssueDto`
- `CourseProgressBlueprintDto`
- `ResolveMaterialTargetsRequest`
- `ResolvedMaterialDto`
- `GetCourseProgressBlueprintsRequest`
- `GetCoursesByGithubOrgRequest`

## 3. Паттерны ответов

### 3.1 Cursor pagination

Для списков используется `CursorResponse<T>`:

- `Items`
- `NextCursor`
- `TotalCount`

Сейчас так работают, например:

- `GET /courses/catalog`
- `GET /courses/my`
- `GET /courses/{courseId}/lessons`
- `GET /articles`

### 3.2 Guid-returning mutation endpoints

Многие mutation endpoint-ы возвращают `Guid`, чаще всего id затронутой сущности или прикреплённого reference.

Примеры:

- create course/module/project/lesson/issue/article;
- attach article to course/module;
- publish/archive/restore operations;
- reorder operations.

## 4. Актуальные contract notes

### 4.1 `CreateCourseRequest`

```json
{
  "title": "string",
  "description": "string",
  "price": 0,
  "gitHubOrgSlug": "string|null"
}
```

### 4.2 `UpdateCourseRequest`

```json
{
  "title": "string",
  "description": "string",
  "price": 0,
  "gitHubOrgSlug": "string|null",
  "detailedDescription": "string|null",
  "learningOutcomes": ["string"],
  "prerequisites": ["string"]
}
```

### 4.3 `CreateArticleRequest` / `UpdateArticleRequest`

```json
{
  "title": "string",
  "content": "markdown",
  "isPublic": true
}
```

### 4.4 `UpdateLessonRequest`

```json
{
  "title": "string",
  "content": "string|null",
  "accessType": "FREE|ENROLLED"
}
```

### 4.5 `UpdateIssueRequest`

```json
{
  "title": "string",
  "content": "markdown",
  "accessType": "FREE|ENROLLED"
}
```

### 4.6 `AttachCourseArticleRequest`

```json
{
  "articleId": "guid"
}
```

### 4.7 `AttachModuleArticleRequest`

```json
{
  "articleId": "guid"
}
```

## 5. Runtime access semantics

### 5.1 Public endpoints не всегда означают публичный контент

`AllowAnonymous` на endpoint не гарантирует, что материал откроется анониму.

Фактическая логика:

- `Lesson` с `FREE` открывается;
- `Lesson` с `ENROLLED` требует entitlement к ресурсу `lesson`;
- `Issue` с `FREE` открывается;
- `Issue` с `ENROLLED` требует entitlement к ресурсу `issue`;
- `Article` detail требует:
  - либо public published article,
  - либо author/manage rights,
  - либо course entitlement к опубликованному курсу, где статья используется.

### 5.2 Private article filtering

Приватные статьи скрываются не только в `GET /articles`, но и во view-моделях:

- `GET /courses/{courseId}/landing`
- `GET /courses/{courseId}/curriculum`
- `GET /modules/{moduleId}/overview`

## 6. Internal API for ProgressService

Этот API нельзя использовать как публичный frontend API.

Он нужен для:

- обогащения enrollment/progress projections;
- вычисления blueprint totals;
- GitHub org sync;
- resolve материалов внутри курса.

Особенности:

- доступен только service/admin ролям;
- рассчитан на батчевые вызовы;
- часть ответов опирается на опубликованное состояние сущностей.

## 7. Что в API пока отсутствует

Несмотря на наличие доменной сущности и EF-конфигурации, на текущий момент нет полноценного публичного API для:

- `Quiz`

Поэтому любые старые документы, где quiz описан как готовый API-раздел, устарели.
