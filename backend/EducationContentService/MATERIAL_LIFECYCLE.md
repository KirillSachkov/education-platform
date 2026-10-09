# Material lifecycle — канонические бизнес-правила

Этот документ фиксирует инварианты `Material`, его размещение в курсах/модулях/подборках,
доступ и media lifecycle. При расхождении документа с тестами или доменной моделью изменение
нельзя мержить молча: контракт и код обновляются вместе.

## Модель

- `materials` — единственный источник содержимого, статуса и `AccessType` материала.
- `course_materials(course_id, material_id)` — единственный источник принадлежности курсу.
- `module_items` — упорядоченное размещение уже привязанного к курсу материала.
- `collection_items` — ссылка из подборки; сама по себе не делает материал курсовым.
- Один материал может находиться в нескольких курсах, модулях и подборках.
- `Content`, `Description`, `VideoId`, `ImageId` независимы: publish-инвариант использует
  только `Content` и `VideoId`; `Description` — отдельный блок полезных материалов.

## Инварианты

| ID | Правило | Где обеспечивается |
|---|---|---|
| INV-1 | Обычный авторский flow не меняет `AuthorId`. Админский transfer курса переносит владельца только у эксклюзивного контента одной ECS-транзакцией; shared material не переносится. | `ReassignCourseAuthor`, repository batch |
| INV-2 | `AccessType` имеет ровно `PUBLIC`, `REGISTERED`, `ENROLLED`. | enum + validators |
| INV-3 | Orphan `ENROLLED` допустим и требует platform-wide `plan:all`; отсутствие вычислимых plan-tags означает fail-closed sentinel, но никогда PUBLIC fallback. | `ContentAccessTagBuilder`, sync handlers |
| INV-4 | `module_items(module, material)` для модуля курса C требует `course_materials(C, material)`. | attach auto-create, detach cascade |
| INV-5 | Title уникален среди non-DRAFT материалов по действующему filtered index/repository contract. | DB index + repository check |
| INV-6 | `PUBLISHED` материал всегда имеет `Content != null` или `VideoId != null`; update не может обойти publish-проверку. | domain aggregate |
| INV-7 | Hard delete атомарно удаляет ECS-ссылки (`module_items`, `course_materials`, `collection_items`, issue JSON refs) и публикует `material.hard_deleted`. | explicit transaction in delete handler |
| INV-8 | Материал можно разместить в нескольких модулях; feed курса дедуплицирует его через `course_materials`. | schema + read queries |
| INV-9 | `(course_id, material_id)` уникален; повторный attach идемпотентен. | unique index + handler |
| INV-10 | Detach последнего курса не меняет `AccessType`; он каскадно удаляет module placement и публикует `material.access_changed`, чтобы orphan plan-tags были пересчитаны. | detach handler + outbox |

## Lifecycle

### Create

- Space-level create: нет `CourseId`, default `PUBLIC`, статус `DRAFT`.
- Create в контексте курса: атомарно создаёт `material`, `course_materials` и, если задан
  `ModuleId`, `module_items`. Default доступа выбирает request/UI; `ENROLLED` допустим.
- Draft asset может быть подготовлен заранее, но становится authoritative только после
  сохранения material media id + `BindingRevision` и durable confirmation.

### Update

- Ownership проверяется до мутаций. Владелец курса может редактировать material своего курса,
  но hard delete остаётся автору/admin/moderator из-за cross-course blast radius.
- Prospective final state проверяется до FileService bind/detach: опубликованный материал нельзя
  оставить одновременно без markdown и видео.
- Media selection хранится как `(assetId, bindingRevision)` с optimistic `MediaVersion`.
  Stale/reordered File events не перезаписывают более новую selection.
- Смена `AccessType` публикует durable `material.access_changed`; Redis — derived index,
  PostgreSQL — source of truth.

### Publish / draft / archive

- Publish разрешён из `DRAFT`/`ARCHIVED` и требует content или video.
- `PublishedAt` выставляется только при первой публикации.
- Send-to-draft/archive публикуют lifecycle event и инвалидируют course curriculum/landing cache.
- Authoritative удаление последнего video у content-less published material переводит его в
  `DRAFT` той же транзакцией и публикует lifecycle event; пустой `PUBLISHED` недопустим.

### Course/module placement

- Attach к модулю сначала гарантирует `course_materials`, затем создаёт `module_items`.
- Detach из курса удаляет все размещения материала в модулях этого курса до удаления binding.
- Transfer module item между курсами переносит derived course binding согласованно.
- Collection membership не создаёт и не удаляет `course_materials`.

### Hard delete

- Удаление выполняется одной ECS-транзакцией вместе с outbox event.
- Consumers очищают Progress, Comments, File assets и Redis access projection
  идемпотентно; save failure должен приводить к retry/DLQ, а не acknowledgement.
- FileService использует revision/tombstone protocol: событие старой revision не удаляет новый asset.

## Access contract

| AccessType | Anonymous | Authenticated | Plan grant / admin |
|---|---:|---:|---:|
| `PUBLIC` | yes | yes | yes |
| `REGISTERED` | no | yes | yes |
| `ENROLLED` | no | no | yes |

- Detail с markdown проходит entitlement, кроме разрешённых author/admin/public short-circuit.
- Metadata feeds могут возвращать published карточки с `isAccessible/lockReason`, но не полный body.
- Course-bound `ENROLLED` получает `plan:course:{id}` + `plan:all`; orphan — `plan:all`.
- Empty/unknown Redis access state всегда fail-closed.

## Обязательные regression scenarios

1. Create в module context атомарно создаёт обе binding-строки.
2. Повторный attach к курсу не создаёт дубликат.
3. Один material в двух модулях остаётся одной feed-карточкой.
4. Detach последнего курса сохраняет `ENROLLED`, удаляет module placement и пересчитывает tags.
5. Published update не удаляет одновременно content и video.
6. Stale bind/detach/delete revision не меняет актуальную media selection.
7. Hard delete очищает все ECS reference types и публикует durable event.
8. Anonymous/registered/enrolled/admin получают ровно доступ из таблицы выше.

Основные тесты: `MaterialLifecycleTests`, `MaterialUseCasesTests`, media event tests,
content-access sync tests и cross-service FileService integration suite.
