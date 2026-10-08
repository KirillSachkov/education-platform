"use client";

import {
  SEARCH_ENTITY_TYPE_FACET_FIELD,
  searchDocumentsCursorInfiniteQueryOptions,
  searchDocumentsInfiniteQueryOptions,
} from "@/entities/search";
import {
  type GlobalSearchEntityType,
  type GlobalSearchTagPayload,
} from "@/shared/lib/global-search-store";
import { SEARCH_DOCUMENTS_PAGE_SIZE } from "../lib/constants";
import { useInfiniteQuery } from "@tanstack/react-query";

interface UseGlobalSearchParams {
  open: boolean;
  search: string;
  selectedEntityType: GlobalSearchEntityType;
  selectedTags: GlobalSearchTagPayload[];
  authorId?: string;
  courseId?: string;
}

export function useGlobalSearch({
  open,
  search,
  selectedEntityType,
  selectedTags,
  authorId,
  courseId,
}: UseGlobalSearchParams) {
  const normalizedTagIds = selectedTags.map((tag) => tag.id).sort();
  const hasTextQuery = search.length > 0;
  const hasDocumentQuery = search.length > 0 || selectedTags.length > 0;
  const effectiveSelectedEntityType = hasDocumentQuery ? selectedEntityType : "All";
  const filters = {
    pageSize: SEARCH_DOCUMENTS_PAGE_SIZE,
    tagIds: normalizedTagIds,
    ...(authorId !== undefined && { authorId }),
    ...(courseId !== undefined && { courseId }),
    ...(effectiveSelectedEntityType !== "All" && {
      entityTypes: [effectiveSelectedEntityType],
    }),
  };

  const relevanceQuery = useInfiniteQuery({
    ...searchDocumentsInfiniteQueryOptions({ ...filters, search }),
    enabled: open && hasDocumentQuery && hasTextQuery,
    placeholderData: (previous) => previous,
  });
  const browseQuery = useInfiniteQuery({
    ...searchDocumentsCursorInfiniteQueryOptions(filters),
    enabled: open && hasDocumentQuery && !hasTextQuery,
    placeholderData: (previous) => previous,
  });
  const documentsQuery = hasTextQuery ? relevanceQuery : browseQuery;

  const firstPage = documentsQuery.data?.pages[0];
  const entityTypeFacets =
    firstPage?.facets.find((facet) => facet.field === SEARCH_ENTITY_TYPE_FACET_FIELD)?.values ?? [];
  const hits = documentsQuery.data?.pages.flatMap((page) => page.hits) ?? [];
  const visibleResultsCount = hits.length;
  const totalResultsCount = firstPage?.totalCount ?? visibleResultsCount;

  return {
    documentsQuery,
    effectiveSelectedEntityType,
    entityTypeFacets,
    hits,
    hasDocumentQuery,
    isError: documentsQuery.isError,
    totalResultsCount,
  };
}
