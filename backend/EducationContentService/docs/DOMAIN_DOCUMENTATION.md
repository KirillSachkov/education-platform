# EducationContentService Domain

## 1. Aggregate roots

Текущие aggregate roots в домене:

- `Course`
- `Module`
- `Lesson`
- `Project`
- `Issue`
- `Article`
- `Quiz`

Join/entity-сущности:

- `CourseItem`
- `CourseArticle`
- `ModuleItem`
- `ProjectItem`

## 2. Иерархия контента

```text
Course
├── CourseItem(Module) -> Module
│   └── ModuleItem(Lesson | Article | Issue | Quiz)
└── CourseItem(Project) -> Project
    └── ProjectItem(Issue)

Course
└── CourseArticle -> Article
```

Это означает:

- `Course` агрегирует разделы верхнего уровня;
- `Module` агрегирует теоретические материалы;
- `Project` агрегирует практические задачи;
- `Article` может использоваться и как global material, и как course-level material, и как module item;
- `Issue` может жить внутри `Project`, а также дополнительно прикрепляться к `Module`;
- `Quiz` подготовлен на уровне домена, но сейчас не имеет полноценного HTTP/use-case слоя.

## 3. Publication model

### 3.1 Общий enum

Большинство агрегатов используют `PublicationStatus`:

- `Draft`
- `Published`
- `Archived`

### 3.2 Правила переходов

`Course`, `Module`, `Lesson`, `Project`, `Issue`:

- `Draft -> Published`
- `Published -> Archived`
- `Archived -> Published` через restore

`Article`:

- `Draft -> Published`
- `Published -> Archived`
- `Archived -> Published`
- `Published|Archived -> Draft`

Отдельный нюанс: у `Article` есть отдельный флаг `IsPublic`, который не совпадает со статусом публикации.

## 4. Access model

### 4.1 `AccessType`

Используется у:

- `Lesson`
- `Issue`

Значения:

- `FREE`
- `ENROLLED`

### 4.2 Articles

У статьи нет `AccessType`. Вместо этого поведение определяется комбинацией:

- `Status`
- `IsPublic`
- связями статьи с опубликованными курсами;
- entitlement пользователя к этим курсам;
- author/manage permissions.

## 5. Aggregate details

### 5.1 Course

`Course` описывает коммерческий продукт и верхний контейнер учебного плана.

Состояние:

- `Id`
- `AuthorId`
- `Title`
- `Description`
- `Price?`
- `PublicationStatus`
- `ImageId?`
- `VideoId?`
- `GitHubOrg?`
- `Slug?`
- `DetailedDescription?`
- `LearningOutcomes[]`
- `Prerequisites[]`
- timestamps

Поведение:

- `Update(...)`
- `Publish()`
- `Archive()`
- `Restore()`
- `AttachImage() / DetachImage()`
- `AttachVideo() / DetachVideo()`

### 5.2 CourseItem

Связывает курс с секцией верхнего уровня:

- `Module`
- `Project`

Содержит:

- `CourseId`
- `ReferenceId`
- `CourseItemType`
- `SortKey`
- `IsOptional`

### 5.3 CourseArticle

Связывает курс с отдельной статьёй из библиотеки материалов курса.

Содержит:

- `CourseId`
- `ArticleId`
- `SortKey`

Важно:

- это отдельная ветка курса, не `CourseItem`;
- course article не заменяет article inside module;
- используется для “полезных материалов” и других course-level article сценариев.

### 5.4 Module

Теоретический контейнер.

Состояние:

- `Id`
- `AuthorId`
- `Title`
- `Description?`
- `DetailedDescription?`
- `PublicationStatus`
- timestamps

Поведение:

- `Update(...)`
- `Publish()`
- `Archive()`
- `Restore()`

### 5.5 ModuleItem

Связывает модуль с атомарным материалом.

Поддерживаемые типы:

- `Lesson`
- `Article`
- `Issue`
- `Quiz`

Состояние:

- `ModuleId`
- `ReferenceId`
- `ModuleItemType`
- `SortKey`
- `IsOptional`
- `ViewPriority`

`ViewPriority` сейчас нужен для UI-представления и приоритизации материала внутри модуля.

### 5.6 Lesson

Видео-урок.

Состояние:

- `Id`
- `AuthorId`
- `Title`
- `Content?`
- `PublicationStatus`
- `AccessType`
- `ImageId?`
- `VideoId?`
- timestamps

Поведение:

- `Update(...)`
- `Publish()`
- `Archive()`
- `Restore()`
- `AttachImage() / DetachImage()`
- `AttachVideo() / DetachVideo()`

Инвариант:

- урок нельзя опубликовать и восстановить без `VideoId`.

### 5.7 Project

Практический контейнер.

Состояние:

- `Id`
- `AuthorId`
- `Title`
- `Description?`
- `DetailedDescription?`
- `PublicationStatus`
- timestamps

Поведение:

- `Update(...)`
- `Publish()`
- `Archive()`
- `Restore()`

### 5.8 ProjectItem

Join-сущность между `Project` и `Issue`.

Содержит:

- `ProjectId`
- `IssueId`
- `SortKey`
- `IsOptional`

### 5.9 Issue

Практическая задача.

Состояние:

- `Id`
- `AuthorId`
- `ProjectId`
- `Title`
- `MarkdownContent`
- `PublicationStatus`
- `AccessType`
- `ExternalLinks[]`
- `InternalMaterials[]`
- timestamps

Поведение:

- `Update(...)`
- `UpdateExternalLinks(...)`
- `UpdateInternalMaterials(...)`
- `Publish()`
- `Archive()`
- `Restore()`

### 5.10 Article

Markdown-статья.

Состояние:

- `Id`
- `AuthorId`
- `Title`
- `MarkdownContent`
- `IsPublic`
- `PublicationStatus`
- timestamps

Поведение:

- `Update(...)`
- `Publish()`
- `Archive()`
- `SendToDraft()`
- `MakePublic()`
- `MakePrivate()`

### 5.11 Quiz

`Quiz` присутствует в домене и persistence-конфигурации, но в текущем сервисе ещё не доведён до полноценного use-case/API уровня. Документация по runtime-потоку quiz должна считаться предварительной.

## 6. Value objects

Основные value objects:

- `Title`
- `Description`
- `DetailedDescription`
- `MarkdownContent`
- `ImageId`
- `VideoId`
- `Price`
- `GitHubOrg`
- `MaxScore`
- `IssueExternalLink`
- `IssueInternalMaterial`
- `Url`
- `ViewPriority`

Смысл:

- централизованная валидация;
- защита от неконсистентных состояний;
- ограничение строковых полей и URL на уровне домена.

## 7. Ключевые инварианты

### 7.1 Lesson

- lesson не публикуется без видео;
- `AccessType` определяет модель runtime-доступа.

### 7.2 Issue

- issue не существует без `ProjectId`;
- issue может иметь internal materials и external links;
- одна и та же issue может участвовать в `ProjectItem` и отдельно прикрепляться к `ModuleItem`.

### 7.3 Article

- доступ определяется не только статусом, но и `IsPublic`;
- приватная статья не обязана быть недоступной абсолютно: она может открываться через entitlement к опубликованному курсу;
- статья может одновременно присутствовать в нескольких курсах и/или модулях.

### 7.4 Ordering

- `CourseItem`, `CourseArticle`, `ModuleItem`, `ProjectItem` не используют integer position;
- порядок хранится через `SortKey`, поэтому reorder нужно делать только через ordering-сервис и соответствующие use case.

### 7.5 Optionality

`IsOptional` есть у join-сущностей внутри курса, модуля и проекта. На текущий момент это влияет на read-model и на progress blueprints для расчёта totals обязательных сущностей.

## 8. Domain boundaries

В домен ECS не входят:

- пользовательские профили и роли;
- факт enrollment;
- фактический прогресс;
- комментарии;
- бинарные файлы и видео-файлы;
- entitlement storage.

Эти данные либо приходят через integration/API, либо резолвятся на read-side.

## 9. Практические последствия для разработки

- при добавлении нового типа материала нужно обновлять не только домен, но и `ModuleItemType`, SQL read-model, DTO, progress lookup и access rules;
- изменения article-домена почти всегда затрагивают `GetArticles`, `GetArticleDetail`, `GetCourseArticles`, `GetCurriculum`, `GetCourseLanding`, `GetModuleOverview`;
- изменения `AccessType` у lesson/issue нужно проверять не только в detail handler, но и в Redis/content-access потоке;
- изменения ordering-сущностей требуют проверки move/transfer endpoint-ов и builder read-model.
