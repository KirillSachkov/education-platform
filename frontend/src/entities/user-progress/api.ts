import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";

export interface MaterialViewStatusDto {
  materialId: string;
  isViewed: boolean;
  /** ISO timestamp of the view; null for non-viewed materials. */
  viewedAt: string | null;
}

/** Per-material viewed timestamp, or `null` if not viewed. */
export interface MaterialViewState {
  isViewed: boolean;
  viewedAt: string | null;
}

export interface GetMaterialViewStatusResponse {
  items: MaterialViewStatusDto[];
}

export const userProgressApi = {
  getMaterialViewStatus: async (
    materialIds: string[],
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.post<Envelope<GetMaterialViewStatusResponse>>(
      "/progress/materials/view-status/",
      { materialIds },
      { signal },
    );
    return res.data.result!;
  },
};

export const userProgressQueryOptions = {
  baseKey: "user-progress",

  materialViewStatusOptions: (materialIds: string[]) => {
    // Sort + dedupe so two callers passing same IDs in different order share a cache entry.
    const normalized = [...new Set(materialIds)].sort();
    return queryOptions({
      queryKey: [userProgressQueryOptions.baseKey, "material-view-status", normalized] as const,
      queryFn: ({ signal }) => userProgressApi.getMaterialViewStatus(normalized, { signal }),
      enabled: normalized.length > 0,
      select: (data) => {
        const map = new Map<string, MaterialViewState>();
        for (const item of data.items) {
          map.set(item.materialId, {
            isViewed: item.isViewed,
            viewedAt: item.viewedAt,
          });
        }
        return map;
      },
    });
  },
};
