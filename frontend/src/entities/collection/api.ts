import { apiClient, type Envelope } from "@/shared/api";
import type { CursorResponse } from "@/shared/api/cursor-response";
import { queryOptions } from "@tanstack/react-query";
import type {
  AddItemRequest,
  AddSectionRequest,
  BulkSetItemsAccessTypeResponse,
  CollectionAccessType,
  CollectionDetailDto,
  CollectionId,
  CollectionSummaryDto,
  CreateCollectionRequest,
  UpdateCollectionRequest,
  UpdateSectionRequest,
} from "./types";

export const collectionsApi = {
  // CRUD
  create: async (request: CreateCollectionRequest): Promise<CollectionId> => {
    const res = await apiClient.post<Envelope<CollectionId>>("/collections/", request);
    return res.data.result!;
  },
  update: async ({
    id,
    request,
  }: {
    id: CollectionId;
    request: UpdateCollectionRequest;
  }): Promise<CollectionId> => {
    const res = await apiClient.put<Envelope<CollectionId>>(`/collections/${id}/`, request);
    return res.data.result!;
  },
  publish: async (id: CollectionId): Promise<CollectionId> => {
    const res = await apiClient.post<Envelope<CollectionId>>(`/collections/${id}/publish/`);
    return res.data.result!;
  },
  sendToDraft: async (id: CollectionId): Promise<CollectionId> => {
    const res = await apiClient.post<Envelope<CollectionId>>(`/collections/${id}/draft/`);
    return res.data.result!;
  },
  archive: async (id: CollectionId): Promise<CollectionId> => {
    const res = await apiClient.post<Envelope<CollectionId>>(`/collections/${id}/archive/`);
    return res.data.result!;
  },
  delete: async (id: CollectionId): Promise<CollectionId> => {
    const res = await apiClient.delete<Envelope<CollectionId>>(`/collections/${id}/`);
    return res.data.result!;
  },

  // Sections
  addSection: async ({
    collectionId,
    request,
  }: {
    collectionId: CollectionId;
    request: AddSectionRequest;
  }): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/collections/${collectionId}/sections/`,
      request,
    );
    return res.data.result!;
  },
  updateSection: async ({
    collectionId,
    sectionId,
    request,
  }: {
    collectionId: CollectionId;
    sectionId: string;
    request: UpdateSectionRequest;
  }): Promise<string> => {
    const res = await apiClient.put<Envelope<string>>(
      `/collections/${collectionId}/sections/${sectionId}/`,
      request,
    );
    return res.data.result!;
  },
  removeSection: async ({
    collectionId,
    sectionId,
  }: {
    collectionId: CollectionId;
    sectionId: string;
  }): Promise<string> => {
    const res = await apiClient.delete<Envelope<string>>(
      `/collections/${collectionId}/sections/${sectionId}/`,
    );
    return res.data.result!;
  },

  // Items
  addItem: async ({
    collectionId,
    sectionId,
    request,
  }: {
    collectionId: CollectionId;
    sectionId: string;
    request: AddItemRequest;
  }): Promise<string> => {
    const res = await apiClient.post<Envelope<string>>(
      `/collections/${collectionId}/sections/${sectionId}/items/`,
      request,
    );
    return res.data.result!;
  },
  removeItem: async ({
    collectionId,
    sectionId,
    itemId,
  }: {
    collectionId: CollectionId;
    sectionId: string;
    itemId: string;
  }): Promise<string> => {
    const res = await apiClient.delete<Envelope<string>>(
      `/collections/${collectionId}/sections/${sectionId}/items/${itemId}/`,
    );
    return res.data.result!;
  },

  /**
   * Bulk-смена AccessType у всех материалов подборки (рекурсивно по секциям).
   * Меняет access у каждого материала, который реально менялся; материалы, уже
   * на нужном AccessType, и материалы чужого автора — пропускаются (см. response).
   */
  bulkSetItemsAccessType: async ({
    collectionId,
    accessType,
  }: {
    collectionId: CollectionId;
    accessType: CollectionAccessType;
  }): Promise<BulkSetItemsAccessTypeResponse> => {
    const res = await apiClient.patch<Envelope<BulkSetItemsAccessTypeResponse>>(
      `/collections/${collectionId}/items/access-type/`,
      { accessType },
    );
    return res.data.result!;
  },

  // Queries
  getDetail: async (
    id: CollectionId,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<CollectionDetailDto> => {
    const res = await apiClient.get<Envelope<CollectionDetailDto>>(`/collections/${id}/detail/`, {
      signal,
    });
    return res.data.result!;
  },
  getMy: async ({
    courseId,
    cursor,
    limit,
    signal,
  }: { courseId?: string; cursor?: string; limit?: number; signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CursorResponse<CollectionSummaryDto>>>(
      "/collections/",
      { params: { courseId, cursor, limit }, signal },
    );
    return res.data;
  },
  getAuthorCollections: async (
    authorId: string,
    { cursor, limit, signal }: { cursor?: string; limit?: number; signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<CollectionSummaryDto>>>(
      `/authors/${authorId}/collections/`,
      { params: { cursor, limit }, signal },
    );
    return res.data;
  },
  getCourseCollections: async (
    courseId: string,
    {
      pinned,
      cursor,
      limit,
      signal,
    }: { pinned?: boolean; cursor?: string; limit?: number; signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<CollectionSummaryDto>>>(
      `/courses/${courseId}/collections/`,
      { params: { pinned, cursor, limit }, signal },
    );
    return res.data;
  },
};

export const collectionsQueryOptions = {
  baseKey: "collections",
};

export const collectionDetailQueryOptions = (id: CollectionId) =>
  queryOptions({
    queryKey: [collectionsQueryOptions.baseKey, id, "detail"] as const,
    queryFn: ({ signal }) => collectionsApi.getDetail(id, { signal }),
    enabled: !!id,
  });

export const myCollectionsQueryOptions = (courseId?: string) =>
  queryOptions({
    queryKey: [collectionsQueryOptions.baseKey, "my", { courseId: courseId ?? null }] as const,
    queryFn: ({ signal }) => collectionsApi.getMy({ courseId, limit: 100, signal }),
    select: (data) => data.result?.items ?? [],
  });

export const authorCollectionsQueryOptions = (authorId: string) =>
  queryOptions({
    queryKey: [collectionsQueryOptions.baseKey, "author", authorId] as const,
    queryFn: ({ signal }) => collectionsApi.getAuthorCollections(authorId, { limit: 100, signal }),
    enabled: !!authorId,
    select: (data) => data.result?.items ?? [],
  });

export const courseCollectionsQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [collectionsQueryOptions.baseKey, "course", courseId] as const,
    queryFn: ({ signal }) => collectionsApi.getCourseCollections(courseId, { limit: 100, signal }),
    enabled: !!courseId,
    select: (data) => data.result?.items ?? [],
  });
