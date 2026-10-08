import { EntityTypes, type EntityType } from "@/shared/config/entity-types";

export const SEARCH_DOCUMENTS_PAGE_SIZE = 20;

export const SEARCH_FACET_ORDER: EntityType[] = [
  EntityTypes.COURSE,
  EntityTypes.MODULE,
  EntityTypes.PROJECT,
  EntityTypes.MATERIAL,
  EntityTypes.COLLECTION,
  EntityTypes.ISSUE,
  EntityTypes.QUIZ,
];

export const SEARCH_FACET_PLURAL_LABELS: Partial<Record<EntityType, string>> = {
  [EntityTypes.COURSE]: "Курсы",
  [EntityTypes.MODULE]: "Модули",
  [EntityTypes.PROJECT]: "Проекты",
  [EntityTypes.MATERIAL]: "Материалы",
  [EntityTypes.COLLECTION]: "Подборки",
  [EntityTypes.ISSUE]: "Задачи",
  [EntityTypes.QUIZ]: "Тесты",
};
