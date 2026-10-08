export type BookmarkTargetType = "Material" | "Lesson" | "Issue";

export interface EntityReferenceDto {
  type: BookmarkTargetType;
  id: string;
}

export interface BookmarkedMaterialDto {
  courseId: string;
  courseSlug: string;
  courseTitle: string;
  target: EntityReferenceDto;
  title: string;
  sectionTitle: string | null;
  sectionType: string;
  createdAt: string;
  /** Есть ли у пользователя grant на ресурс сейчас (enrollment мог быть отозван). */
  isAccessible: boolean;
  /**
   * `null` если доступ есть. Иначе: `not_enrolled` — нужна (ре-)запись на курс.
   * Endpoint auth-required, поэтому `anonymous` не бывает.
   */
  lockReason: "not_enrolled" | null;
}

export interface BookmarkIdDto {
  courseId: string;
  target: EntityReferenceDto;
  createdAt: string;
}

export interface GetBookmarksRequest {
  cursor?: string;
  limit: number;
  courseId?: string;
  entityType?: BookmarkTargetType;
}

export interface GetMyBookmarkIdsRequest {
  courseIds?: string;
  cursor?: string;
  limit?: number;
}
