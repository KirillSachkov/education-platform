import {
  apiClient,
  type CursorResponse,
  type Envelope,
  type PaginationResponse,
} from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AddTagsToEntityRequest,
  CreateTagRequest,
  GetEntityTagsFilters,
  GetPopularTagsFilters,
  GetTagAliasesFilters,
  GetTagsFilters,
  MergeTagsRequest,
  RemoveAliasesRequest,
  RemoveTagsFromEntityRequest,
  SuggestTagsFilters,
  TagDto,
  TagId,
  UpdateTagRequest,
} from "./types";

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 50;
const DEFAULT_SUGGEST_PAGE_SIZE = 20;
const DEFAULT_POPULAR_TAGS_LIMIT = 10;

export const tagsApi = {
  getTags: async (
    filters: GetTagsFilters,
    { signal }: { signal: AbortSignal },
  ) => {
    const res = await apiClient.get<Envelope<CursorResponse<TagDto>>>(
      "/tags/",
      {
        params: {
          limit: filters.limit,
          cursor: filters.cursor || undefined,
          search: filters.search,
          kind: filters.kind,
          authorId: filters.authorId,
        },
        signal,
      },
    );
    return res.data;
  },

  getTagById: async (tagId: TagId, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<TagDto>>(`/tags/${tagId}`, {
      signal,
    });
    return res.data;
  },

  getTagsBatch: async (tagIds: TagId[], { signal }: { signal?: AbortSignal } = {}) => {
    // ASP.NET Core [AsParameters] Guid[] ждёт повторяющийся ключ (tagIds=a&tagIds=b),
    // дефолтный axios-сериализатор массивов (tagIds[]=) бэкенд не распарсит.
    const params = new URLSearchParams();
    for (const id of tagIds) {
      params.append("tagIds", id);
    }
    const res = await apiClient.get<Envelope<TagDto[]>>("/tags/batch/", {
      params,
      signal,
    });
    return res.data;
  },

  getTagAliases: async (
    tagId: TagId,
    filters: GetTagAliasesFilters = {},
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const normalizedFilters = {
      page: filters.page ?? DEFAULT_PAGE,
      pageSize: filters.pageSize ?? DEFAULT_PAGE_SIZE,
      search: filters.search?.trim() || undefined,
    };

    const res = await apiClient.get<Envelope<PaginationResponse<TagDto>>>(
      `/tags/${tagId}/aliases`,
      {
        params: normalizedFilters,
        signal,
      },
    );
    return res.data;
  },

  createTag: async (request: CreateTagRequest) => {
    const res = await apiClient.post<Envelope<string>>("/tags/", request);
    return res.data;
  },

  updateTag: async ({
    tagId,
    request,
  }: {
    tagId: TagId;
    request: UpdateTagRequest;
  }) => {
    const res = await apiClient.patch<Envelope<string>>(`/tags/${tagId}`, request);
    return res.data;
  },

  deleteTag: async (tagId: TagId) => {
    const res = await apiClient.delete<Envelope<string>>(`/tags/${tagId}`);
    return res.data;
  },

  getEntityTags: async (
    filters: GetEntityTagsFilters,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const normalizedFilters = {
      ...filters,
      page: filters.page ?? DEFAULT_PAGE,
      pageSize: filters.pageSize ?? DEFAULT_PAGE_SIZE,
    };

    const res = await apiClient.get<Envelope<PaginationResponse<TagDto>>>(
      "/tags/entity",
      {
        params: normalizedFilters,
        signal,
      },
    );
    return res.data;
  },

  suggestTags: async (
    filters: SuggestTagsFilters,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const normalizedFilters = {
      ...filters,
      page: filters.page ?? DEFAULT_PAGE,
      pageSize: filters.pageSize ?? DEFAULT_SUGGEST_PAGE_SIZE,
    };

    const res = await apiClient.get<Envelope<PaginationResponse<TagDto>>>(
      "/tags/suggest",
      {
        params: normalizedFilters,
        signal,
      },
    );
    return res.data;
  },

  getPopularTags: async (
    filters: GetPopularTagsFilters = {},
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const normalizedFilters = {
      limit: filters.limit ?? DEFAULT_POPULAR_TAGS_LIMIT,
    };

    const res = await apiClient.get<Envelope<TagDto[]>>("/tags/popular", {
      params: normalizedFilters,
      signal,
    });
    return res.data;
  },

  addTagsToEntity: async (request: AddTagsToEntityRequest) => {
    const res = await apiClient.post<Envelope<string>>("/tags/entity", {
      entityType: request.entityType,
      entityId: request.entityId,
      tagIds: request.tagIds ?? [],
      tagTitles: request.tagTitles ?? [],
    });
    return res.data;
  },

  removeTagsFromEntity: async (request: RemoveTagsFromEntityRequest) => {
    const res = await apiClient.delete<Envelope<string>>("/tags/entity", {
      data: {
        entityType: request.entityType,
        entityId: request.entityId,
        tagIds: request.tagIds,
      },
    });
    return res.data;
  },

  mergeTags: async ({
    tagId,
    request,
  }: {
    tagId: TagId;
    request: MergeTagsRequest;
  }) => {
    const res = await apiClient.post<Envelope<string>>(
      `/tags/${tagId}/aliases`,
      request,
    );
    return res.data;
  },

  removeAliases: async ({
    tagId,
    request,
  }: {
    tagId: TagId;
    request: RemoveAliasesRequest;
  }) => {
    const res = await apiClient.delete<Envelope<string>>(
      `/tags/${tagId}/aliases`,
      {
        data: request,
      },
    );
    return res.data;
  },
};

export const tagsQueryOptions = {
  baseKey: "tags",

  list: (filters: GetTagsFilters) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "list", filters],
      queryFn: ({ signal }) => tagsApi.getTags(filters, { signal }),
    }),

  byId: (tagId: TagId) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "by-id", tagId],
      queryFn: ({ signal }) => tagsApi.getTagById(tagId, { signal }),
      enabled: !!tagId,
    }),

  byIds: (tagIds: TagId[]) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "by-ids", tagIds],
      queryFn: ({ signal }) => tagsApi.getTagsBatch(tagIds, { signal }),
      // Backend не гарантирует порядок — восстанавливаем порядок входных id
      // (важно для гидрации filter-чипов из URL).
      select: (data) => {
        const byId = new Map((data.result ?? []).map((tag) => [tag.id, tag]));
        return tagIds.map((id) => byId.get(id)).filter((tag): tag is TagDto => !!tag);
      },
      enabled: tagIds.length > 0,
    }),

  aliasesPage: (
    tagId: TagId,
    filters: GetTagAliasesFilters = {},
  ) =>
    queryOptions({
      queryKey: [
        tagsQueryOptions.baseKey,
        "aliases-page",
        tagId,
        filters,
      ],
      queryFn: ({ signal }) =>
        tagsApi.getTagAliases(tagId, filters, { signal }),
      enabled: !!tagId,
    }),

  aliases: (tagId: TagId, filters: GetTagAliasesFilters = {}) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "aliases", tagId, filters],
      queryFn: ({ signal }) => tagsApi.getTagAliases(tagId, filters, { signal }),
      select: (data) => data.result?.items ?? [],
      enabled: !!tagId,
    }),

  entityTags: (filters: GetEntityTagsFilters) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "entity-tags", filters],
      queryFn: ({ signal }) => tagsApi.getEntityTags(filters, { signal }),
      select: (data) => data.result?.items ?? [],
      enabled: !!filters.entityId,
    }),

  suggest: (filters: SuggestTagsFilters) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "suggest", filters],
      queryFn: ({ signal }) => tagsApi.suggestTags(filters, { signal }),
      select: (data) => data.result?.items ?? [],
      enabled: !!filters.search?.trim(),
    }),

  popular: (filters: GetPopularTagsFilters = {}) =>
    queryOptions({
      queryKey: [tagsQueryOptions.baseKey, "popular", filters],
      queryFn: ({ signal }) => tagsApi.getPopularTags(filters, { signal }),
      select: (data) => data.result ?? [],
    }),

  mergeCandidates: ({
    search,
    pageSize = DEFAULT_PAGE_SIZE,
  }: {
    search?: string;
    pageSize?: number;
  }) =>
    queryOptions({
      queryKey: [
        tagsQueryOptions.baseKey,
        "merge-candidates",
        { search, pageSize },
      ],
      queryFn: ({ signal }) =>
        tagsApi.getTags(
          {
            limit: pageSize,
            search,
            kind: "canon",
          },
          { signal },
        ),
      select: (data) => data.result?.items ?? [],
    }),
};
