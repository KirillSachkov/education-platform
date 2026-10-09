import { apiClient, type Envelope } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { infiniteQueryOptions, keepPreviousData, queryOptions } from "@tanstack/react-query";
import type {
  CreateDraftMaterialRequest,
  CreateMaterialRequest,
  GetMaterialsRequest,
  MaterialBindingsDto,
  MaterialDetailDto,
  MaterialFeedItemDto,
  MaterialFeedScope,
  MaterialId,
  MaterialKind,
  MaterialScope,
  MaterialSummaryDto,
  UpdateMaterialRequest,
} from "./types";

export const materialsApi = {
  getMaterials: async (request: GetMaterialsRequest, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CursorResponse<MaterialSummaryDto>>>("/materials/", {
      params: request,
      signal,
    });
    return res.data;
  },

  getAuthorMaterials: async (
    authorId: string,
    {
      cursor,
      limit,
      kind,
      search,
      signal,
    }: {
      cursor?: string;
      limit?: number;
      kind?: MaterialKind;
      search?: string;
      signal?: AbortSignal;
    } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<MaterialSummaryDto>>>(
      `/authors/${authorId}/materials/`,
      { params: { cursor, limit, kind, search }, signal },
    );
    return res.data;
  },

  getCourseMaterials: async (
    courseId: string,
    {
      cursor,
      limit,
      kind,
      search,
      signal,
    }: {
      cursor?: string;
      limit?: number;
      kind?: MaterialKind;
      search?: string;
      signal?: AbortSignal;
    } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<MaterialSummaryDto>>>(
      `/courses/${courseId}/materials/`,
      { params: { cursor, limit, kind, search }, signal },
    );
    return res.data;
  },

  getMaterialDetail: async (
    materialId: MaterialId,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<MaterialDetailDto> => {
    const res = await apiClient.get<Envelope<MaterialDetailDto>>(
      `/materials/${materialId}/detail/`,
      { signal },
    );
    return res.data.result!;
  },

  createMaterial: async (request: CreateMaterialRequest): Promise<MaterialId> => {
    const res = await apiClient.post<Envelope<MaterialId>>("/materials/", request);
    return res.data.result!;
  },

  createDraftMaterial: async (request: CreateDraftMaterialRequest = {}): Promise<MaterialId> => {
    const res = await apiClient.post<Envelope<MaterialId>>("/materials/draft/", request);
    return res.data.result!;
  },

  updateMaterial: async ({
    materialId,
    request,
  }: {
    materialId: MaterialId;
    request: UpdateMaterialRequest;
  }): Promise<MaterialId> => {
    const res = await apiClient.patch<Envelope<MaterialId>>(`/materials/${materialId}/`, request);
    return res.data.result!;
  },

  publishMaterial: async (
    materialId: MaterialId,
    options?: { notifySubscribers?: boolean },
  ): Promise<MaterialId> => {
    const res = await apiClient.post<Envelope<MaterialId>>(`/materials/${materialId}/publish/`, {
      notifySubscribers: options?.notifySubscribers ?? true,
    });
    return res.data.result!;
  },

  sendMaterialToDraft: async (materialId: MaterialId): Promise<MaterialId> => {
    const res = await apiClient.post<Envelope<MaterialId>>(`/materials/${materialId}/draft/`);
    return res.data.result!;
  },

  archiveMaterial: async (materialId: MaterialId): Promise<MaterialId> => {
    const res = await apiClient.post<Envelope<MaterialId>>(`/materials/${materialId}/archive/`);
    return res.data.result!;
  },

  deleteMaterial: async (materialId: MaterialId): Promise<MaterialId> => {
    const res = await apiClient.delete<Envelope<MaterialId>>(`/materials/${materialId}/`);
    return res.data.result!;
  },

  getAuthorMaterialsFeed: async (
    authorId: string,
    {
      cursor,
      limit = 15,
      kind,
      scope = "all",
      search,
      signal,
    }: {
      cursor?: string;
      limit?: number;
      kind?: MaterialKind;
      scope?: MaterialFeedScope;
      search?: string;
      signal?: AbortSignal;
    } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<MaterialFeedItemDto>>>(
      `/authors/${authorId}/materials/feed/`,
      {
        params: { cursor, limit, kind, scope, search },
        paramsSerializer: { indexes: null },
        signal,
      },
    );
    return res.data;
  },

  getCourseMaterialsFeed: async (
    courseId: string,
    {
      cursor,
      limit = 15,
      kind,
      search,
      signal,
    }: {
      cursor?: string;
      limit?: number;
      kind?: MaterialKind;
      search?: string;
      signal?: AbortSignal;
    } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<MaterialFeedItemDto>>>(
      `/courses/${courseId}/materials/feed/`,
      {
        params: { cursor, limit, kind, search },
        paramsSerializer: { indexes: null },
        signal,
      },
    );
    return res.data;
  },
};

export const materialsQueryOptions = {
  baseKey: "materials",

  getListInfiniteOptions: ({
    scope,
    limit = 12,
    kind,
    search,
    accessFilter,
  }: {
    scope: MaterialScope;
    limit?: number;
    kind?: MaterialKind;
    search?: string;
    accessFilter?: "free";
  }) =>
    infiniteQueryOptions({
      queryKey: [
        materialsQueryOptions.baseKey,
        "list",
        scope,
        {
          limit,
          kind: kind ?? null,
          search: search ?? null,
          accessFilter: accessFilter ?? null,
        },
      ] as const,
      queryFn: ({ pageParam, signal }) =>
        materialsApi.getMaterials(
          { scope, limit, kind, search, accessFilter, cursor: pageParam },
          { signal },
        ),
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
      // Keep the previous page visible while a new search/kind filter resolves —
      // search-as-you-type stays smooth instead of flashing the spinner on every keystroke.
      placeholderData: keepPreviousData,
      select: (data) => ({
        items: data.pages.flatMap((page) => page.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    }),
};

export const materialBindingsQueryOptions = (materialId: MaterialId) =>
  queryOptions({
    queryKey: [materialsQueryOptions.baseKey, materialId, "bindings"] as const,
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<MaterialBindingsDto>>(
        `/materials/${materialId}/bindings/`,
        { signal },
      );
      return res.data.result!;
    },
    enabled: !!materialId,
  });

export const materialDetailQueryOptions = (materialId: MaterialId) =>
  queryOptions({
    queryKey: [materialsQueryOptions.baseKey, materialId, "detail"] as const,
    queryFn: ({ signal }) => materialsApi.getMaterialDetail(materialId, { signal }),
    // Держим предыдущий материал на экране пока грузится новый — навигация
    // между уроками в sidebar выглядит мгновенной, без flash на спиннер.
    placeholderData: keepPreviousData,
    enabled: !!materialId,
  });

export const authorMaterialsQueryOptions = (
  authorId: string,
  { limit = 12, kind, search }: { limit?: number; kind?: MaterialKind; search?: string } = {},
) =>
  infiniteQueryOptions({
    queryKey: [
      materialsQueryOptions.baseKey,
      "author",
      authorId,
      { limit, kind: kind ?? null, search: search ?? null },
    ] as const,
    queryFn: ({ pageParam, signal }) =>
      materialsApi.getAuthorMaterials(authorId, {
        cursor: pageParam,
        limit,
        kind,
        search,
        signal,
      }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    enabled: !!authorId,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
      totalCount: data.pages[0]?.result?.totalCount ?? 0,
    }),
  });

export const courseMaterialsQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [materialsQueryOptions.baseKey, "course", courseId] as const,
    queryFn: ({ signal }) => materialsApi.getCourseMaterials(courseId, { signal }),
    enabled: !!courseId,
    select: (data) => data.result?.items ?? [],
  });

export const courseMaterialIdsQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [materialsQueryOptions.baseKey, "course", courseId, "ids"] as const,
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<string[]>>(`/courses/${courseId}/materials/ids/`, {
        signal,
      });
      return res.data.result ?? [];
    },
    enabled: !!courseId,
  });

export const authorMaterialsFeedQueryOptions = (
  authorId: string,
  {
    limit = 15,
    kind,
    scope = "all",
    search,
  }: {
    limit?: number;
    kind?: MaterialKind;
    scope?: MaterialFeedScope;
    search?: string;
  } = {},
) => {
  return infiniteQueryOptions({
    queryKey: [
      materialsQueryOptions.baseKey,
      "feed",
      "author",
      authorId,
      {
        limit,
        kind: kind ?? null,
        scope,
        search: search ?? null,
      },
    ] as const,
    queryFn: ({ pageParam, signal }) =>
      materialsApi.getAuthorMaterialsFeed(authorId, {
        cursor: pageParam,
        limit,
        kind,
        scope,
        search,
        signal,
      }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    enabled: !!authorId,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
    }),
  });
};

export const courseMaterialsFeedQueryOptions = (
  courseId: string,
  {
    limit = 15,
    kind,
    search,
  }: {
    limit?: number;
    kind?: MaterialKind;
    search?: string;
  } = {},
) => {
  return infiniteQueryOptions({
    queryKey: [
      materialsQueryOptions.baseKey,
      "feed",
      "course",
      courseId,
      {
        limit,
        kind: kind ?? null,
        search: search ?? null,
      },
    ] as const,
    queryFn: ({ pageParam, signal }) =>
      materialsApi.getCourseMaterialsFeed(courseId, {
        cursor: pageParam,
        limit,
        kind,
        search,
        signal,
      }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    enabled: !!courseId,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
    }),
  });
};

export const courseMaterialsInfiniteOptions = (
  courseId: string,
  { limit = 20, kind, search }: { limit?: number; kind?: MaterialKind; search?: string } = {},
) =>
  infiniteQueryOptions({
    queryKey: [
      materialsQueryOptions.baseKey,
      "course",
      courseId,
      { limit, kind: kind ?? null, search: search ?? null },
    ] as const,
    queryFn: ({ pageParam, signal }) =>
      materialsApi.getCourseMaterials(courseId, {
        cursor: pageParam,
        limit,
        kind,
        search,
        signal,
      }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.result?.nextCursor ?? undefined,
    enabled: !!courseId,
    select: (data) => ({
      items: data.pages.flatMap((page) => page.result?.items ?? []),
      totalCount: data.pages[0]?.result?.totalCount ?? 0,
    }),
  });
