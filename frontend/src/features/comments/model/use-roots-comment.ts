import { commentsQueryOptions } from "@/entities/comment";
import type { EntityType } from "@/shared/config/entity-types";
import { useInfiniteScroll } from "@/shared/hooks";
import { useInfiniteQuery } from "@tanstack/react-query";

type UseRootsCommentOptions = {
  targetType: EntityType;
  targetId: string;
  enabled?: boolean;
};

export function useRootsComment(options: UseRootsCommentOptions) {
  const { targetId, targetType, enabled } = options;

  const {
    data,
    isLoading,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
    ...commentsQueryOptions.getRootsCommentInfiniteOptions({
      limit: 10,
      targetId,
      targetType,
    }),
    enabled: enabled && !!targetId,
  });

  const cursorRef = useInfiniteScroll({
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  });

  return {
    data,
    isLoading,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
    cursorRef,
  };
}
