import type { MaterialSummaryDto } from "@/entities/material";

export type CollectionId = string;
export type CollectionStatus = "DRAFT" | "PUBLISHED" | "ARCHIVED";

/**
 * Совпадает с backend enum {@link EducationContentService.Domain.AccessType}.
 * Issue #358: FREE удалён, остались PUBLIC/REGISTERED/ENROLLED.
 */
export type CollectionAccessType = "PUBLIC" | "REGISTERED" | "ENROLLED";

/**
 * Стабильный контракт с бэком — см. {@link ContentAccess.LockReasons} в C#.
 * anonymous — нужно войти; trial_required — legacy course-trial (резолвер тот же,
 * что у материалов: CollectionAccessEnricher/GetCollectionDetail зовут
 * LockReasonResolver.Resolve, который ещё эмитит это значение на course:X:trial
 * тегах); standard_required — требуется полная запись; not_enrolled — нужна
 * запись на курс; plan_required — нужен план доступа.
 */
export type CollectionLockReason =
  | "anonymous"
  | "trial_required"
  | "standard_required"
  | "not_enrolled"
  | "plan_required";

export interface CollectionSummaryDto {
  id: CollectionId;
  authorId: string;
  title: string;
  description: string | null;
  coverImageUrl: string | null;
  itemCount: number;
  courseId: string | null;
  courseTitle: string | null;
  courseSlug: string | null;
  status: CollectionStatus;
  accessType: CollectionAccessType;
  isAccessible: boolean;
  lockReason: CollectionLockReason | null;
  isPinned: boolean;
  pinnedSortKey: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CollectionDetailDto {
  id: CollectionId;
  authorId: string;
  title: string;
  description: string | null;
  coverImageId: string | null;
  coverImageUrl: string | null;
  courseId: string | null;
  courseTitle: string | null;
  courseSlug: string | null;
  status: CollectionStatus;
  accessType: CollectionAccessType;
  /**
   * Доступен ли header подборки полноценно (PUBLIC, REGISTERED+auth, или enrollment).
   * При `false` карточка может всё равно быть видна (если хоть один item открыт),
   * а внутри detail рендерим header в локированном стиле, но саму структуру (sections + items)
   * показываем — каждый item имеет собственный `isAccessible/lockReason`.
   */
  isAccessible: boolean;
  lockReason: CollectionLockReason | null;
  createdAt: string;
  updatedAt: string;
  sections: CollectionSectionDto[];
}

export interface CollectionSectionDto {
  id: string;
  title: string | null;
  description: string | null;
  items: CollectionItemDto[];
}

/** Тип элемента подборки (#491 — generic items). */
export type CollectionItemType = "MATERIAL" | "QUIZ";

export interface CollectionItemDto {
  id: string;
  /** Id материала (`itemType=MATERIAL`) или квиза (`itemType=QUIZ`). */
  referenceId: string;
  itemType: CollectionItemType;
  /** Карточка материала; `null` для QUIZ-items. */
  material: MaterialSummaryDto | null;
  /** Заголовок квиза; `null` для MATERIAL-items. */
  quizTitle: string | null;
  /** Число вопросов квиза; `null` для MATERIAL-items. */
  questionsCount: number | null;
  /**
   * Per-item access — даже в гейтнутой подборке PUBLIC материалы остаются доступны.
   * Используется для рендера замка на конкретной карточке материала внутри подборки.
   */
  isAccessible: boolean;
  lockReason: CollectionLockReason | null;
}

export interface CreateCollectionRequest {
  title: string;
  description?: string | null;
  courseId?: string | null;
  accessType?: CollectionAccessType | null;
}

export interface UpdateCollectionRequest {
  title: string;
  description?: string | null;
  accessType: CollectionAccessType;
  /**
   * Желаемое состояние обложки. `null` ⇒ открепить (sync detach через FileService),
   * значение ⇒ привязать (idempotent).
   */
  coverId?: string | null;
}

export interface AddSectionRequest {
  title?: string | null;
  description?: string | null;
}

export interface UpdateSectionRequest {
  title?: string | null;
  description?: string | null;
}

/**
 * Добавление элемента в секцию (#491 — generic). `itemType` не передан /
 * `null` ⇒ MATERIAL (back-compat со старыми payload'ами).
 */
export interface AddItemRequest {
  referenceId: string;
  itemType?: CollectionItemType;
}

/**
 * Итог bulk-смены AccessType. Соответствует backend
 * `BulkSetItemsAccessTypeResponse`. Используется для toast'а:
 * «N обновлено, M уже были» — даёт автору ясный сигнал, что произошло.
 */
export interface BulkSetItemsAccessTypeResponse {
  updatedCount: number;
  skippedCount: number;
  /** Сколько материалов чужого автора пропущено (cross-author подборки). */
  skippedNotOwnedCount: number;
  totalCount: number;
}
