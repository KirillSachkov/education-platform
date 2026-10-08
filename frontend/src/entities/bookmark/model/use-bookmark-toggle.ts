"use client";

import {
  useMutation,
  useQueryClient,
  type InfiniteData,
  type QueryKey,
} from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { bookmarksApi, bookmarkQueryOptions } from "../api";
import type { BookmarkIdDto, BookmarkTargetType } from "../types";

interface UseBookmarkToggleParams {
  courseId: string;
  entityType: BookmarkTargetType;
  entityId: string;
}

type BookmarkIdsCache = InfiniteData<CursorResponse<BookmarkIdDto>>;

export function useBookmarkToggle({ courseId, entityType, entityId }: UseBookmarkToggleParams) {
  const queryClient = useQueryClient();
  const myIdsKeyPrefix: QueryKey = [bookmarkQueryOptions.baseKey, "my-ids"];

  const mutation = useMutation({
    mutationFn: async (isBookmarked: boolean) => {
      if (isBookmarked) {
        return bookmarksApi.deleteBookmark(courseId, entityType, entityId);
      }

      return bookmarksApi.putBookmark(courseId, entityType, entityId);
    },
    // Optimistic toggle: patch every `my-ids` infinite query that already
    // includes (or could include) this target. Avoids a refetch round-trip
    // while bookmark UI in lists/feeds reflects the toggle immediately.
    onMutate: async (wasBookmarked) => {
      await queryClient.cancelQueries({ queryKey: myIdsKeyPrefix });

      const snapshots: Array<[QueryKey, BookmarkIdsCache | undefined]> = [];
      const caches = queryClient.getQueriesData<BookmarkIdsCache>({
        queryKey: myIdsKeyPrefix,
      });

      for (const [key, data] of caches) {
        if (!data) continue;
        snapshots.push([key, data]);
        const next: BookmarkIdsCache = {
          ...data,
          pages: wasBookmarked
            ? data.pages.map((page, idx) => ({
                ...page,
                items: page.items.filter(
                  (item) =>
                    !(
                      item.courseId === courseId &&
                      item.target.type === entityType &&
                      item.target.id === entityId
                    ),
                ),
                // Only mutate the first page's totalCount snapshot (see prependBookmark).
                totalCount: idx === 0 ? Math.max(0, page.totalCount - 1) : page.totalCount,
              }))
            : prependBookmark(data, { courseId, entityType, entityId }),
        };
        queryClient.setQueryData<BookmarkIdsCache>(key, next);
      }

      return { snapshots };
    },
    onError: (error, _wasBookmarked, context) => {
      for (const [key, data] of context?.snapshots ?? []) {
        queryClient.setQueryData(key, data);
      }
      toast.error(getErrorMessage(error, "Не удалось обновить закладку"));
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: [bookmarkQueryOptions.baseKey, "list"],
        }),
        queryClient.invalidateQueries({ queryKey: myIdsKeyPrefix }),
      ]);
    },
  });

  return {
    toggleBookmark: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}

function prependBookmark(
  data: BookmarkIdsCache,
  target: { courseId: string; entityType: BookmarkTargetType; entityId: string },
): BookmarkIdsCache["pages"] {
  const newItem: BookmarkIdDto = {
    courseId: target.courseId,
    target: { type: target.entityType, id: target.entityId },
    createdAt: new Date().toISOString(),
  };

  // totalCount is a per-page snapshot of the full filtered set; only mutate
  // the first page (the one a renderer reads) to avoid drift on later pages.
  return data.pages.map((page, idx) =>
    idx === 0
      ? { ...page, items: [newItem, ...page.items], totalCount: page.totalCount + 1 }
      : page,
  );
}
