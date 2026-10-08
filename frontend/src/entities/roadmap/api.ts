import { apiClient, type Envelope } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  CreateRoadmapRequest,
  RoadmapDto,
  RoadmapProgressItemRequest,
  RoadmapProgressResponse,
  RoadmapSummaryDto,
  SaveCanvasRequest,
  UpdateRoadmapRequest,
} from "./types";

export const roadmapQueryKeys = {
  base: "roadmaps",
  progress: "roadmap-progress",
};

export const roadmapsApi = {
  getRoadmap: async (
    roadmapId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<RoadmapDto>>(
      `/roadmaps/${roadmapId}/`,
      { signal },
    );
    return res.data;
  },

  getRoadmapByCourse: async (
    courseId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<RoadmapDto | null>>(
      `/roadmaps/by-course/${courseId}/`,
      { signal },
    );
    return res.data;
  },

  getRoadmapBySlug: async (
    slug: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<RoadmapDto>>(
      `/roadmaps/by-slug/${slug}/`,
      { signal },
    );
    return res.data;
  },

  getRoadmaps: async (
    params?: { courseId?: string; standaloneOnly?: boolean },
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<RoadmapSummaryDto>>>(
      "/roadmaps/",
      { params, signal },
    );
    return res.data;
  },

  createRoadmap: async (request: CreateRoadmapRequest) => {
    const res = await apiClient.post<Envelope<string>>(
      "/roadmaps/",
      request,
    );
    return res.data;
  },

  updateRoadmap: async ({
    roadmapId,
    request,
  }: {
    roadmapId: string;
    request: UpdateRoadmapRequest;
  }) => {
    const res = await apiClient.put<Envelope<string>>(
      `/roadmaps/${roadmapId}/`,
      request,
    );
    return res.data;
  },

  saveCanvas: async ({
    roadmapId,
    request,
  }: {
    roadmapId: string;
    request: SaveCanvasRequest;
  }) => {
    const res = await apiClient.put<Envelope<string>>(
      `/roadmaps/${roadmapId}/canvas/`,
      request,
    );
    return res.data;
  },

  publishRoadmap: async (roadmapId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/roadmaps/${roadmapId}/publish/`,
    );
    return res.data;
  },

  archiveRoadmap: async (roadmapId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/roadmaps/${roadmapId}/archive/`,
    );
    return res.data;
  },

  restoreRoadmap: async (roadmapId: string) => {
    const res = await apiClient.post<Envelope<string>>(
      `/roadmaps/${roadmapId}/restore/`,
    );
    return res.data;
  },

  deleteRoadmap: async (roadmapId: string) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/roadmaps/${roadmapId}/`,
    );
    return res.data;
  },

  getRoadmapProgress: async (
    items: RoadmapProgressItemRequest[],
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.post<Envelope<RoadmapProgressResponse>>(
      "/progress/roadmap-progress/",
      { items },
      { signal },
    );
    return res.data;
  },

  getByAuthor: async (
    authorId: string,
    { cursor, limit, signal }: { cursor?: string; limit?: number; signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<RoadmapSummaryDto>>>(
      `/roadmaps/by-author/${authorId}/`,
      { params: { cursor, limit }, signal },
    );
    return res.data;
  },
};

export const roadmapQueryOptions = (roadmapId: string) =>
  queryOptions({
    queryKey: [roadmapQueryKeys.base, roadmapId],
    queryFn: ({ signal }) => roadmapsApi.getRoadmap(roadmapId, { signal }),
    select: (data) => data.result!,
    enabled: !!roadmapId,
  });

export const roadmapByCourseQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [roadmapQueryKeys.base, "by-course", courseId],
    queryFn: ({ signal }) =>
      roadmapsApi.getRoadmapByCourse(courseId, { signal }),
    select: (data) => data.result ?? null,
    enabled: !!courseId,
  });

export const roadmapBySlugQueryOptions = (slug: string) =>
  queryOptions({
    queryKey: [roadmapQueryKeys.base, "by-slug", slug],
    queryFn: ({ signal }) => roadmapsApi.getRoadmapBySlug(slug, { signal }),
    select: (data) => data.result!,
    enabled: !!slug,
  });

export const roadmapsListQueryOptions = (params?: {
  courseId?: string;
  standaloneOnly?: boolean;
}) =>
  queryOptions({
    queryKey: [roadmapQueryKeys.base, "list", params],
    queryFn: ({ signal }) => roadmapsApi.getRoadmaps(params, { signal }),
    select: (data) => data.result?.items ?? [],
  });

export const authorRoadmapsQueryOptions = (authorId: string) =>
  queryOptions({
    queryKey: [roadmapQueryKeys.base, "by-author", authorId],
    queryFn: ({ signal }) =>
      roadmapsApi.getByAuthor(authorId, { limit: 100, signal }),
    select: (data) => data.result?.items ?? [],
    enabled: !!authorId,
  });

export const authorRoadmapsInfiniteOptions = (
  authorId: string,
  { limit = 20 }: { limit?: number } = {},
) =>
  infiniteQueryOptions({
    queryKey: [roadmapQueryKeys.base, "by-author", authorId, { limit }] as const,
    queryFn: ({ pageParam, signal }) =>
      roadmapsApi.getByAuthor(authorId, { cursor: pageParam, limit, signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    enabled: !!authorId,
    select: (data) => ({
      items: data.pages.flatMap((p) => p.result?.items ?? []),
      totalCount: data.pages[0]?.result?.totalCount ?? 0,
    }),
  });
