import type {
  MaterialAccessType,
  MaterialFeedItemDto,
  MaterialKind,
  MaterialLockReason,
  MaterialStatus,
} from "@/entities/material";
import type { SearchEducationDocumentDto, SearchLockReason } from "@/entities/search";
import { EntityTypes } from "@/shared/config/entity-types";
import { fileImageSrc } from "@/shared/lib/file-src";

/**
 * Адаптер `SearchEducationDocumentDto → MaterialFeedItemDto` для КБ-списка.
 * Позволяет переиспользовать `MaterialCard` без развилок по источнику данных —
 * браузим ли мы через Typesense cursor-пагинацию или получаем relevance-выдачу
 * по текстовому запросу, наружу уходит один и тот же DTO.
 *
 * Маппинг «на лучшее доступное»:
 * - `preview` — пусто (у Typesense нет 280-char preview, есть highlight-сниппет,
 *   который рендерится отдельным слотом в КБ для search-режима).
 * - `thumbnailUrl` — резолвим cover тем же приоритетом, что и ECS-фид и Ctrl+K
 *   (`SearchItem`): кастомная обложка (`imageId` → `/api/files/{id}/content`),
 *   иначе Kinescope poster (`videoThumbnailUrl`), иначе null. `MaterialCard`
 *   рендерит только `thumbnailUrl` и сам imageId не резолвит, поэтому URL
 *   собираем здесь — без этого ручные обложки в КБ не показывались.
 * - `accessType`/`status`/`createdAt` — значения «по умолчанию»: в search-документе
 *   этих полей нет, но для рендера карточки они не требуются.
 */
export function searchDocumentToMaterialFeedItem(
  doc: SearchEducationDocumentDto,
): MaterialFeedItemDto {
  if (doc.entityType !== EntityTypes.MATERIAL) {
    throw new Error(
      `searchDocumentToMaterialFeedItem: expected entityType=material, got ${doc.entityType}`,
    );
  }

  return {
    id: doc.entityId,
    authorId: doc.authorId ?? "",
    title: doc.title,
    preview: null,
    kind: (doc.materialKind ?? "ARTICLE") as MaterialKind,
    status: "PUBLISHED" satisfies MaterialStatus,
    accessType: "PUBLIC" satisfies MaterialAccessType,
    createdAt: doc.updatedAtUtc,
    updatedAt: doc.updatedAtUtc,
    publishedAt: doc.updatedAtUtc,
    imageId: doc.imageId ?? null,
    videoId: null,
    thumbnailUrl: doc.imageId ? fileImageSrc(doc.imageId) : (doc.videoThumbnailUrl ?? null),
    moduleTitle: doc.moduleTitle ?? null,
    courseId: doc.courseId ?? null,
    courseTitle: doc.courseTitle ?? null,
    courseSlug: doc.courseSlug ?? null,
    isAccessible: doc.isAccessible,
    lockReason: mapSearchLockReason(doc.lockReason),
  };
}

function mapSearchLockReason(
  reason: SearchLockReason | null,
): MaterialLockReason | null {
  if (reason === null) return null;
  // SearchLockReason и MaterialLockReason используют идентичные литералы.
  return reason satisfies MaterialLockReason;
}
