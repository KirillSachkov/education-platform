import { apiClient, type Envelope } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { infiniteQueryOptions } from "@tanstack/react-query";
import type {
  BookmarkIdDto,
  BookmarkedMaterialDto,
  BookmarkTargetType,
  GetBookmarksRequest,
  GetMyBookmarkIdsRequest,
} from "./types";

export const bookmarksApi = {
  putBookmark: async (
    courseId: string,
    entityType: BookmarkTargetType,
    entityId: string,
  ) => {
    const res = await apiClient.put<Envelope<string>>(
      `/progress/courses/${courseId}/bookmarks/${entityType}/${entityId}/`,
    );
    return res.data.result!;
  },

  deleteBookmark: async (
    courseId: string,
    entityType: BookmarkTargetType,
    entityId: string,
  ) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/progress/courses/${courseId}/bookmarks/${entityType}/${entityId}/`,
    );
    return res.data.result;
  },

  getBookmarks: async (
    request: GetBookmarksRequest,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<BookmarkedMaterialDto>>>(
      "/progress/bookmarks/",
      { params: request, signal },
    );
    return res.data.result!;
  },

  getMyBookmarkIds: async (
    request: GetMyBookmarkIdsRequest,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<BookmarkIdDto>>>(
      "/progress/bookmarks/me/ids/",
      { params: request, signal },
    );
    return res.data.result!;
  },
};

const MY_BOOKMARK_IDS_PAGE_SIZE = 200;

export const bookmarkQueryOptions = {
  baseKey: "bookmarks",

  listKey: (filters?: {
    limit?: number;
    courseId?: string;
    entityType?: BookmarkTargetType;
  }) => [bookmarkQueryOptions.baseKey, "list", filters ?? {}] as const,

  myIdsKey: (courseIds: readonly string[]) =>
    [
      bookmarkQueryOptions.baseKey,
      "my-ids",
      [...courseIds].sort().join(","),
    ] as const,

  getBookmarksInfiniteOptions: ({
    limit,
    courseId,
    entityType,
  }: {
    limit: number;
    courseId?: string;
    entityType?: BookmarkTargetType;
  }) =>
    infiniteQueryOptions({
      queryKey: bookmarkQueryOptions.listKey({ limit, courseId, entityType }),
      queryFn: ({ pageParam, signal }) =>
        bookmarksApi.getBookmarks(
          {
            limit,
            cursor: pageParam,
            courseId,
            entityType,
          },
          { signal },
        ),
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
      select: (data) => ({
        items: data.pages.flatMap((page) => page.items),
        totalCount: data.pages[0]?.totalCount ?? 0,
      }),
    }),

  getMyBookmarkIdsInfiniteOptions: (
    courseIds: readonly string[],
    enabled = true,
  ) =>
    infiniteQueryOptions({
      queryKey: bookmarkQueryOptions.myIdsKey(courseIds),
      queryFn: ({ pageParam, signal }) =>
        bookmarksApi.getMyBookmarkIds(
          {
            courseIds: courseIds.length > 0 ? courseIds.join(",") : undefined,
            cursor: pageParam,
            limit: MY_BOOKMARK_IDS_PAGE_SIZE,
          },
          { signal },
        ),
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
      enabled,
      select: (data) => data.pages.flatMap((page) => page.items),
    }),
};
