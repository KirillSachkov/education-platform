import { commentsQueryOptions, type CommentDto, type CommentId } from "@/entities/comment";
import type { Envelope } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import type { EntityType } from "@/shared/config/entity-types";
import { useInfiniteQuery } from "@tanstack/react-query";

const CHILDREN_PAGE_SIZE = 20;

type UseChildrenCommentOptions = {
  parentId: CommentId;
  targetType: EntityType;
  targetId: string;
  enabled?: boolean;
  /**
   * Превью первых N ответов, заинлайненное в root-листинг (см. `CommentDto.previewChildren`).
   * Если задано — служит первой страницей `useInfiniteQuery` (initialData), и сетевой
   * вызов происходит только когда юзер нажимает «Показать ещё ответы».
   */
  initialItems?: CommentDto[];
  /** Cursor для дозагрузки после initialItems (см. `CommentDto.previewChildrenNextCursor`). */
  initialNextCursor?: string | null;
  /** Реальное общее число прямых ответов (`CommentDto.childrenCount`) — для seed.totalCount. */
  initialTotalCount?: number;
};

export function useChildrenComment(options: UseChildrenCommentOptions) {
  const {
    targetId,
    targetType,
    enabled,
    parentId,
    initialItems,
    initialNextCursor,
    initialTotalCount,
  } = options;

  const hasSeed = !!initialItems && initialItems.length > 0;

  // initialData seeds первый page так, чтобы queryFn НЕ вызывался при mount —
  // данные уже есть с сервера (вошли вместе с root). Сетевой запрос возникает
  // только при `fetchNextPage`, когда юзер хочет догрузить остаток.
  const seedPage: Envelope<CursorResponse<CommentDto>> | null = hasSeed
    ? {
        result: {
          items: initialItems!,
          nextCursor: initialNextCursor ?? undefined,
          // Реальный children-count, а не размер preview — иначе `select` отдаёт
          // stale totalCount даже после fetchNextPage (тот не пере-вычисляет).
          totalCount: initialTotalCount ?? initialItems!.length,
        },
        error: null,
        isError: false,
        timeGenerated: new Date().toISOString(),
      }
    : null;

  const {
    data,
    isLoading,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
    ...commentsQueryOptions.getChildrenCommentInfiniteOptions({
      limit: CHILDREN_PAGE_SIZE,
      targetId,
      targetType,
      parentId,
    }),
    enabled: enabled && !!targetId && !!parentId,
    ...(seedPage
      ? {
          initialData: {
            pages: [seedPage],
            pageParams: [undefined as string | undefined],
          },
          // Без staleTime initialData считается мгновенно устаревшим, и React Query
          // запустит queryFn в фоне → потеряем смысл seed'а.
          staleTime: 60_000,
        }
      : {}),
  });

  return {
    data,
    isLoading,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  };
}
