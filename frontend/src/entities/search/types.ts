import type { EntityType } from "@/shared/config/entity-types";

export type SearchLockReason =
  | "anonymous"
  // Legacy course-trial resource sets — LockReasonResolver всё ещё эмитит это значение
  // (Shared/ContentAccess/.../LockReasonResolver.cs возвращает TRIAL_REQUIRED на
  // course:X:trial тегах). Без него resolveLockCopy уходил в default-ветку с неверным CTA.
  | "trial_required"
  | "standard_required"
  | "not_enrolled"
  | "plan_required";

export interface SearchEducationDocumentDto {
  entityId: string;
  entityType: EntityType;
  title: string;
  description?: string | null;
  imageId?: string | null;
  courseId?: string | null;
  courseSlug?: string | null;
  courseTitle?: string | null;
  projectId?: string | null;
  projectTitle?: string | null;
  moduleId?: string | null;
  moduleTitle?: string | null;
  tagIds: string[];
  tagTitles: string[];
  updatedAtUtc: string;
  isAccessible: boolean;
  lockReason: SearchLockReason | null;
  /** `ARTICLE` / `VIDEO` / `NOTE` / `STREAM` — только для entityType === MATERIAL. */
  materialKind?: string | null;
  /** Kinescope-thumbnail для VIDEO; бэк резолвит через FileService batch. */
  videoThumbnailUrl?: string | null;
  /** AuthorId — для scope-фильтрации и маппинга в MaterialFeedItemDto. */
  authorId?: string | null;
  /**
   * Заголовки глав видео (Kinescope chapters). Параллельный массив с
   * `chapterTimestamps`. Только для VIDEO; для остальных типов пустой/отсутствует.
   */
  chapterTitles?: string[];
  /**
   * Offset'ы глав в секундах (parallel с `chapterTitles`). По индексу совпавшей
   * главы из `SearchHighlight.matchedIndices` фронт собирает `?t=<seconds>` для
   * deep-link на нужный момент в Kinescope-плеере.
   */
  chapterTimestamps?: number[];
}

export interface SearchHighlight {
  field: string;
  snippet: string;
  /**
   * Индексы совпавших элементов для array-полей (например `chapter_titles`).
   * Параллельны позициям в `document.chapterTitles` / `document.chapterTimestamps`.
   * Для скалярных полей `null`/`undefined`.
   */
  matchedIndices?: number[] | null;
}

export interface SearchFacetValue {
  value: string;
  count: number;
}

export interface SearchFacet {
  field: string;
  values: SearchFacetValue[];
}

export interface SearchHit<TDocument> {
  document: TDocument;
  score?: number | null;
  highlights: SearchHighlight[];
}

export interface SearchDocumentsResponse {
  hits: SearchHit<SearchEducationDocumentDto>[];
  facets: SearchFacet[];
  totalCount: number;
  page: number;
  pageSize: number;
  /** Keyset-курсор для следующей страницы в browse-режиме. null, если конец выдачи или relevance-mode. */
  nextCursor?: string | null;
}

export interface SearchDocumentsFilters {
  search?: string;
  page?: number;
  pageSize?: number;
  cursor?: string;
  courseId?: string;
  authorId?: string;
  tagIds?: string[];
  entityTypes?: EntityType[];
  /** Фильтр по material kind (ARTICLE/VIDEO/NOTE/STREAM). Передаётся в backend filter_by. */
  materialKind?: string;
  /**
   * Витринный фильтр доступа. `"free"` — PUBLIC или AUTHENTICATED; `"public"` —
   * только материалы, доступные анониму без регистрации. Grants не учитываются.
   */
  accessFilter?: "free" | "public";
}
