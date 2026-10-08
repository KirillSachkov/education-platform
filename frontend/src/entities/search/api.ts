import { apiClient, type Envelope } from "@/shared/api";
import { EntityTypes, type EntityType } from "@/shared/config/entity-types";
import { infiniteQueryOptions, keepPreviousData, queryOptions } from "@tanstack/react-query";
import type {
  SearchDocumentsFilters,
  SearchDocumentsResponse,
  SearchEducationDocumentDto,
  SearchFacet,
  SearchHit,
} from "./types";

const DEFAULT_PAGE = 1;
const DEFAULT_PAGE_SIZE = 20;
export const SEARCH_ENTITY_TYPE_FACET_FIELD = "entity_type";

type RawSearchEducationDocumentDto = Omit<
  SearchEducationDocumentDto,
  "entityType"
> & {
  entityType: string;
};

type RawSearchDocumentsResponse = Omit<
  SearchDocumentsResponse,
  "hits" | "facets"
> & {
  hits: SearchHit<RawSearchEducationDocumentDto>[];
  facets: SearchFacet[];
};

const SEARCH_ENTITY_TYPE_MAP: Record<string, EntityType> = {
  Course: EntityTypes.COURSE,
  course: EntityTypes.COURSE,
  Module: EntityTypes.MODULE,
  module: EntityTypes.MODULE,
  Project: EntityTypes.PROJECT,
  project: EntityTypes.PROJECT,
  Material: EntityTypes.MATERIAL,
  material: EntityTypes.MATERIAL,
  Issue: EntityTypes.ISSUE,
  issue: EntityTypes.ISSUE,
  Quiz: EntityTypes.QUIZ,
  quiz: EntityTypes.QUIZ,
  Collection: EntityTypes.COLLECTION,
  collection: EntityTypes.COLLECTION,
};

function normalizeSearchEntityType(value: string): EntityType {
  return SEARCH_ENTITY_TYPE_MAP[value] ?? (value as EntityType);
}

function normalizeSearchResponse(
  data: Envelope<RawSearchDocumentsResponse>,
): Envelope<SearchDocumentsResponse> {
  if (!data.result) {
    return data as Envelope<SearchDocumentsResponse>;
  }

  return {
    ...data,
    result: {
      ...data.result,
      hits: data.result.hits.map((hit) => ({
        ...hit,
        document: {
          ...hit.document,
          entityType: normalizeSearchEntityType(hit.document.entityType),
        },
      })),
      facets: data.result.facets.map((facet) =>
        facet.field === SEARCH_ENTITY_TYPE_FACET_FIELD
          ? {
              ...facet,
              values: facet.values.map((item) => ({
                ...item,
                value: normalizeSearchEntityType(item.value),
              })),
            }
          : facet,
      ),
    },
  };
}

function normalizeTagIds(tagIds?: string[]) {
  return [...(tagIds ?? [])].sort();
}

function normalizeSearchFilters(filters: SearchDocumentsFilters) {
  return {
    page: filters.page ?? DEFAULT_PAGE,
    pageSize: filters.pageSize ?? DEFAULT_PAGE_SIZE,
    cursor: filters.cursor || undefined,
    courseId: filters.courseId,
    authorId: filters.authorId,
    search: filters.search?.trim() || undefined,
    tagIds: normalizeTagIds(filters.tagIds),
    entityTypes: normalizeEntityTypes(filters.entityTypes),
    materialKind: filters.materialKind || undefined,
    accessFilter: filters.accessFilter || undefined,
  };
}

function normalizeEntityTypes(entityTypes?: EntityType[]) {
  return [...(entityTypes ?? [])].sort();
}

function getSearchDocumentsQueryKey(filters: SearchDocumentsFilters) {
  return [searchQueryOptions.baseKey, "documents", normalizeSearchFilters(filters)] as const;
}

function getSearchDocumentsInfiniteQueryKey(filters: SearchDocumentsFilters) {
  return [
    searchQueryOptions.baseKey,
    "documents-infinite",
    normalizeSearchFilters({ ...filters, page: undefined }),
  ] as const;
}

// Backend SearchService биндит ?entityTypes=... в `EntityType[]` (C# enum с PascalCase
// именами). Дефолтный ASP.NET model binder для enum-массивов регистрозависим — lowercase
// `material` отваливается 400-м без тела. Фронт исторически держит значения в lowercase
// (EntityTypes.COURSE = "course"), поэтому перед отправкой приводим к PascalCase.
function toBackendEntityType(value: string): string {
  return value.length > 0 ? value.charAt(0).toUpperCase() + value.slice(1) : value;
}

export const searchApi = {
  getDocuments: async (
    filters: SearchDocumentsFilters,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const normalized = normalizeSearchFilters(filters);
    const response = await apiClient.get<Envelope<RawSearchDocumentsResponse>>(
      "/search/",
      {
        params: {
          ...normalized,
          entityTypes: normalized.entityTypes.map(toBackendEntityType),
        },
        paramsSerializer: { indexes: null },
        signal,
      },
    );
    return normalizeSearchResponse(response.data);
  },
};

export const searchQueryOptions = {
  baseKey: "search",
};

export const searchDocumentsQueryOptions = (filters: SearchDocumentsFilters) =>
  queryOptions({
    queryKey: getSearchDocumentsQueryKey(filters),
    queryFn: ({ signal }) => searchApi.getDocuments(filters, { signal }),
    select: (data: Envelope<SearchDocumentsResponse>) => data.result!,
  });

// Typesense по умолчанию ограничивает page * per_page ≤ max_hits (10 000) —
// дальше 400 Bad Request. Для relevance-выдачи это нормально: никто не листает
// 500 страниц «похожих материалов». Для browse используй keyset (ниже).
const TYPESENSE_MAX_HITS = 10_000;

export const searchDocumentsInfiniteQueryOptions = (
  filters: SearchDocumentsFilters,
) =>
  infiniteQueryOptions({
    queryKey: getSearchDocumentsInfiniteQueryKey(filters),
    initialPageParam: DEFAULT_PAGE,
    placeholderData: keepPreviousData,
    queryFn: async ({ pageParam, signal }) => {
      const data = await searchApi.getDocuments(
        {
          ...filters,
          page: pageParam,
          pageSize: filters.pageSize ?? DEFAULT_PAGE_SIZE,
        },
        { signal },
      );

      return data.result!;
    },
    getNextPageParam: (lastPage) => {
      const loadedCount = lastPage.page * lastPage.pageSize;
      const cap = Math.min(lastPage.totalCount, TYPESENSE_MAX_HITS);
      return loadedCount < cap ? lastPage.page + 1 : undefined;
    },
  });

/**
 * Keyset cursor-пагинация (browse-режим). Используется для КБ, когда нет текстового
 * запроса — обходит потолок page*per_page ≤ max_hits (10 000) и даёт линейную глубину
 * по updated_at_ticks:desc. Для relevance-поиска (search непустой) НЕ применять —
 * backend игнорирует cursor.
 */
export const searchDocumentsCursorInfiniteQueryOptions = (
  filters: SearchDocumentsFilters,
) =>
  infiniteQueryOptions({
    queryKey: [
      searchQueryOptions.baseKey,
      "documents-cursor",
      normalizeSearchFilters({ ...filters, cursor: undefined, page: undefined }),
    ] as const,
    initialPageParam: undefined as string | undefined,
    placeholderData: keepPreviousData,
    queryFn: async ({ pageParam, signal }) => {
      const data = await searchApi.getDocuments(
        {
          ...filters,
          cursor: pageParam,
          pageSize: filters.pageSize ?? DEFAULT_PAGE_SIZE,
        },
        { signal },
      );
      return data.result!;
    },
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  });
