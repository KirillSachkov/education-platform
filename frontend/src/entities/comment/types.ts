import type { EntityType } from "@/shared/config/entity-types";

export type CommentId = string;

export interface CommentDto {
  id: CommentId;
  authorId: string;
  authorName: string | null;
  authorUsername: string | null;
  authorAvatarId: string | null;
  content: string;
  depth: number;
  parentId: string | null;
  parentPreview: string | null;
  createdAt: string;
  updatedAt: string;
  isDeleted: boolean;
  hasMoreChildren: boolean;
  /**
   * Общее количество прямых ответов (excluding soft-deleted). Для root-комментов
   * показывается на UI как «N ответов». Для children/thread всегда 0.
   */
  childrenCount: number;
  /**
   * Первые N (3) прямых ответов, заинлайненные в root-листинг для авто-раскрытия
   * 1-го уровня без N+1 запросов. Заполняется только в `GET /comments/`.
   * Для children/thread — null.
   */
  previewChildren: CommentDto[] | null;
  /**
   * Cursor для дозагрузки оставшихся ответов через `GET /comments/{id}` после
   * исчерпания `previewChildren`. null когда preview вместил все ответы.
   */
  previewChildrenNextCursor: string | null;
}

export interface GetRootsCommentRequest {
  targetType: EntityType;
  targetId: string;
  cursor?: string;
  limit: number;
}

export interface GetChildrenCommentRequest {
  targetType: EntityType;
  targetId: string;
  cursor?: string;
  limit: number;
}

export interface GetThreadCommentRequest {
  targetType: EntityType;
  targetId: string;
  cursor?: string;
  limit: number;
}

export type TargetEntity = {
  type: EntityType;
  id: string;
};

export interface CreateCommentRequest {
  entityReference: TargetEntity;
  content: string;
  parentId?: string;
}

export interface UpdateCommentRequest {
  id: CommentId;
  content: string;
}

export interface InboxCommentDto {
  id: CommentId;
  authorId: string;
  authorName: string | null;
  authorUsername: string | null;
  authorAvatarId: string | null;
  content: string | null;
  depth: number;
  createdAt: string;
  updatedAt: string;
  isDeleted: boolean;
  targetEntityType: EntityType;
  targetEntityId: string;
  parentId: string | null;
  parentAuthorId: string | null;
  parentPreview: string | null;
  /**
   * Slug первого опубликованного курса, к которому привязан целевой материал.
   * Если задан — карточка ведёт на course-scoped material view (с комментами и
   * программой курса), иначе — на standalone KB material view.
   */
  targetCourseSlug: string | null;
}

/**
 * Запись из ленты комментариев автора (`/comments/author-feed/`).
 */
export interface AuthorFeedCommentDto {
  id: CommentId;
  authorId: string;
  authorName: string | null;
  authorUsername: string | null;
  authorAvatarId: string | null;
  content: string | null;
  depth: number;
  createdAt: string;
  updatedAt: string;
  isDeleted: boolean;
  targetEntityType: EntityType;
  targetEntityId: string;
  parentId: string | null;
  parentPreview: string | null;
  hasMyReply: boolean;
  isUnread: boolean;
  targetTitle: string | null;
}

export interface GetAuthorFeedRequest {
  cursor?: string;
  limit?: number;
  withoutReply?: boolean;
  unreadOnly?: boolean;
}

/**
 * Цепочка предков комментария — от root до прямого родителя (сам коммент НЕ включён).
 * Возвращается из `GET /comments/{commentId}/ancestors`. Используется фронтом, чтобы
 * на deep-link `?focus=<id>` форс-раскрыть все collapsed-родителей до того, как
 * браузер прыгнет к таргету.
 */
export interface CommentAncestorsDto {
  targetType: EntityType;
  targetId: string;
  depth: number;
  /** root → … → direct parent. Пустая для root-комментариев. */
  ancestorIds: string[];
}
