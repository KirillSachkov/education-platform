"use client";

import {
  materialsQueryOptions,
  type MaterialKind,
  type MaterialSummaryDto,
} from "@/entities/material";
import { useInfiniteScroll } from "@/shared/hooks";
import { useInfiniteQuery } from "@tanstack/react-query";

const DEFAULT_LIMIT = 12;

export type MaterialsViewMode = "learning" | "teaching";
export type MaterialsTab = "mine" | "public" | "private";

export function useMaterialsFeed({
  mode,
  activeTab,
  limit = DEFAULT_LIMIT,
  accessFilter,
  search,
  kind,
}: {
  mode: MaterialsViewMode;
  activeTab: MaterialsTab;
  limit?: number;
  accessFilter?: "free";
  search?: string;
  kind?: MaterialKind;
}) {
  const scope = mode === "learning" ? "public" : activeTab;

  const materialsQuery = useInfiniteQuery({
    ...materialsQueryOptions.getListInfiniteOptions({ scope, limit, accessFilter, search, kind }),
    enabled: !!scope,
  });

  const items: MaterialSummaryDto[] = materialsQuery.data?.items ?? [];
  const totalCount = materialsQuery.data?.totalCount ?? 0;

  const isLoading = materialsQuery.isLoading;
  const isFetching = materialsQuery.isFetching;
  const isFetchingNextPage = materialsQuery.isFetchingNextPage;
  const hasNextPage = materialsQuery.hasNextPage;
  const error = materialsQuery.error ?? null;

  const fetchNextPage = async () => {
    if (!materialsQuery.hasNextPage || materialsQuery.isFetchingNextPage) {
      return;
    }
    await materialsQuery.fetchNextPage();
  };

  const refetch = async () => {
    await materialsQuery.refetch();
  };

  const cursorRef = useInfiniteScroll({
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage: () => {
      void fetchNextPage();
    },
  });

  return {
    items,
    totalCount,
    isLoading,
    isFetching,
    isFetchingNextPage,
    hasNextPage,
    error,
    fetchNextPage,
    refetch,
    cursorRef,
  };
}
