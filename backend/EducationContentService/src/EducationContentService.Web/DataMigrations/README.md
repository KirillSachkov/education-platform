# Data Migrations

Идемпотентные SQL-скрипты для бизнес-данных: backfill, нормализация, bulk-UPDATE.

## Когда сюда класть

- Любые `UPDATE` / `INSERT` / `DELETE` по бизнес-данным.
- Backfill новых колонок (`UPDATE ... SET new_col = old_col`).
- Нормализация legacy значений.
- **Schema changes остаются в EF Core migrations** (CREATE TABLE, ALTER COLUMN, CREATE INDEX). Только data.

## Правила

- **Имя файла:** `YYYYMMDD_NNN_description.sql`. Применяются по алфавиту, NNN раздвигает коллизии по дате.
- **Идемпотентность обязательна.** Скрипт должен быть безопасен при повторном прогоне:
  - `UPDATE ... WHERE col != 'new_value'` — не обновит уже обновлённое.
  - `INSERT ... SELECT ... WHERE NOT EXISTS (SELECT 1 ...)` — не создаст дубликат.
  - `CREATE INDEX IF NOT EXISTS` (если индекс — всё-таки, но обычно это schema).
- **Не писать `BEGIN` / `COMMIT`** — runner сам оборачивает в транзакцию.
- **Один скрипт = одно логическое изменение.** Если нужно несколько шагов — несколько файлов.
- **Build Action = `EmbeddedResource`** в `.csproj` (настроено группой в `EducationContentService.Web.csproj`).

## Как применяются

1. Migration контейнер сначала запускает `efbundle` (EF schema migrations).
2. Потом `dotnet EducationContentService.Web.dll migrate-data`.
3. `DataMigrationRunner` читает все `.sql` из embedded resources, сортирует по имени, проверяет таблицу `education.__data_migrations_history` и применяет только новые.

## Откат

- **Данные**: ручной rollback-SQL (отдельным скриптом или из backup). Runner не поддерживает Down — data changes часто необратимы (удалённый PascalCase не восстановишь без backup).
- **Трекинг**: удалить строку из `__data_migrations_history`, и скрипт применится снова. Но это нужно редко — скрипты идемпотентны.

## Примеры

### Пример: backfill с NOT EXISTS

```sql
-- 20260419_001_backfill_course_materials_from_module_items.sql
INSERT INTO education.course_materials (id, course_id, material_id, sort_key)
SELECT gen_random_uuid(), ci.course_id, mi.reference_id,
       'BACKFILL_' || row_number() OVER (PARTITION BY ci.course_id ORDER BY mi.sort_key)
FROM education.module_items mi
JOIN education.course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
WHERE mi.item_type = 'Material'
  AND NOT EXISTS (
      SELECT 1 FROM education.course_materials cm
      WHERE cm.course_id = ci.course_id AND cm.material_id = mi.reference_id
  );
```

### Пример: нормализация значения

```sql
-- 20260420_001_normalize_material_kind_casing.sql
UPDATE education.materials SET kind = 'ARTICLE' WHERE kind IN ('Article', 'article');
UPDATE education.materials SET kind = 'VIDEO'   WHERE kind IN ('Video', 'video');
UPDATE education.materials SET kind = 'NOTE'    WHERE kind IN ('Note', 'note');
UPDATE education.materials SET kind = 'STREAM'  WHERE kind IN ('Stream', 'stream');
```
