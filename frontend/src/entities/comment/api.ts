import { apiClient, type Envelope } from "@/shared/api";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  AuthorFeedCommentDto,
  CommentAncestorsDto,
  CommentDto,
  CommentId,
  CreateCommentRequest,
  GetAuthorFeedRequest,
  GetChildrenCommentRequest,
  GetRootsCommentRequest,
  GetThreadCommentRequest,
  InboxCommentDto,
  UpdateCommentRequest,
} from "./types";
import type { EntityType } from "@/shared/config/entity-types";
import type { CursorResponse } from "@/shared/api/cursor-response";

export const commentsApi = {
  getRootsComment: async (request: GetRootsCommentRequest, { signal }: { signal: AbortSignal }) => {
    const res = await apiClient.get<Envelope<CursorResponse<CommentDto>>>("/comments/", {
      params: request,
      signal,
    });

    return res.data;
  },

  getChildrenComment: async (
    parentId: CommentId,
    request: GetChildrenCommentRequest,
    { signal }: { signal: AbortSignal },
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<CommentDto>>>("/comments/" + parentId, {
      params: request,
      signal,
    });

    return res.data;
  },

  getThreadComment: async (
    parentId: CommentId,
    request: GetThreadCommentRequest,
    { signal }: { signal: AbortSignal },
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<CommentDto>>>(
      `/comments/${parentId}/thread`,
      {
        params: request,
        signal,
      },
    );

    return res.data;
  },

  createComment: async (request: CreateCommentRequest) => {
    const res = await apiClient.post<Envelope<string>>("/comments/", request);

    return res.data;
  },

  updateComment: async (request: UpdateCommentRequest) => {
    const res = await apiClient.put<Envelope<string>>(`/comments/${request.id}`, {
      content: request.content,
    });

    return res.data;
  },

  deleteComment: async (id: CommentId) => {
    const res = await apiClient.delete<Envelope<string>>(`/comments/${id}`);

    return res.data;
  },

  getInbox: async (limit: number, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<InboxCommentDto[]>>("/comments/inbox", {
      params: { limit },
      signal,
    });

    return res.data;
  },

  getAuthorFeed: async (
    request: GetAuthorFeedRequest,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<AuthorFeedCommentDto>>>(
      "/comments/author-feed/",
      { params: request, signal },
    );
    return res.data;
  },

  markAuthorFeedViewed: async () => {
    const res = await apiClient.post<Envelope<string>>("/comments/author-feed/mark-viewed/");
    return res.data;
  },

  getCommentAncestors: async (
    commentId: CommentId,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CommentAncestorsDto>>(
      `/comments/${commentId}/ancestors`,
      { signal },
    );
    return res.data;
  },
};

export const commentAncestorsQueryOptions = (commentId: CommentId | null | undefined) =>
  queryOptions({
    queryKey: ["comments", "ancestors", commentId],
    queryFn: ({ signal }) => commentsApi.getCommentAncestors(commentId!, { signal }),
    select: (data) => data.result!,
    enabled: !!commentId,
    staleTime: 60_000,
  });

export const commentInboxQueryOptions = (limit: number) =>
  queryOptions({
    queryKey: [commentsQueryOptions.baseKey, "inbox", limit],
    queryFn: ({ signal }) => commentsApi.getInbox(limit, { signal }),
    select: (data) => data.result ?? [],
    staleTime: 60_000,
  });

export const authorFeedInfiniteQueryOptions = (filter: {
  limit: number;
  withoutReply: boolean;
  unreadOnly: boolean;
}) =>
  infiniteQueryOptions({
    queryKey: ["comments", "author-feed", filter],
    queryFn: ({ pageParam, signal }) =>
      commentsApi.getAuthorFeed(
        {
          limit: filter.limit,
          withoutReply: filter.withoutReply,
          unreadOnly: filter.unreadOnly,
          cursor: pageParam,
        },
        { signal },
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
    }),
    staleTime: 30_000,
  });

export const commentsQueryOptions = {
  baseKey: "comments",

  getRootsCommentInfiniteOptions: (filter: {
    targetType: EntityType;
    targetId: string;
    limit: number;
  }) => {
    return infiniteQueryOptions({
      queryKey: [commentsQueryOptions.baseKey, filter],
      queryFn: ({ pageParam, signal }) => {
        const request: GetRootsCommentRequest = {
          ...filter,
          cursor: pageParam,
        };
        return commentsApi.getRootsComment(request, { signal });
      },
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => {
        const result = lastPage.result;
        if (!result) return undefined;
        return result.nextCursor ? result.nextCursor : undefined;
      },
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    });
  },

  getChildrenCommentInfiniteOptions: (
    filter: {
      targetType: EntityType;
      targetId: string;
      limit: number;
    } & { parentId: string },
  ) => {
    return infiniteQueryOptions({
      queryKey: [commentsQueryOptions.baseKey, "children", filter],
      queryFn: ({ pageParam, signal }) => {
        const { parentId, ...param } = filter;
        const request: GetChildrenCommentRequest = {
          ...param,
          cursor: pageParam,
        };

        return commentsApi.getChildrenComment(parentId, request, { signal });
      },
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => {
        const result = lastPage.result;
        if (!result) return undefined;
        return result.nextCursor ? result.nextCursor : undefined;
      },
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    });
  },

  getThreadCommentInfiniteOptions: (
    filter: {
      targetType: EntityType;
      targetId: string;
      limit: number;
    } & { parentId: string },
  ) => {
    return infiniteQueryOptions({
      queryKey: [commentsQueryOptions.baseKey, "thread", filter],
      queryFn: ({ pageParam, signal }) => {
        const { parentId, ...param } = filter;
        const request: GetThreadCommentRequest = {
          ...param,
          cursor: pageParam,
        };

        return commentsApi.getThreadComment(parentId, request, { signal });
      },
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => {
        const result = lastPage.result;
        if (!result) return undefined;
        return result.nextCursor ? result.nextCursor : undefined;
      },
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    });
  },
};
