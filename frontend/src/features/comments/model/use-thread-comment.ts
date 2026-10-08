import { commentsQueryOptions, type CommentId } from "@/entities/comment";
import type { EntityType } from "@/shared/config/entity-types";
import { useInfiniteQuery } from "@tanstack/react-query";

const THREAD_PAGE_SIZE = 20;

type UseThreadCommentOptions = {
  parentId: CommentId;
  targetType: EntityType;
  targetId: string;
  enabled?: boolean;
};

export function useThreadComment(options: UseThreadCommentOptions) {
  const { targetId, targetType, enabled, parentId } = options;

  const {
    data,
    isLoading,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
    ...commentsQueryOptions.getThreadCommentInfiniteOptions({
      limit: THREAD_PAGE_SIZE,
      targetId,
      targetType,
      parentId,
    }),
    enabled: enabled && !!targetId && !!parentId,
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
