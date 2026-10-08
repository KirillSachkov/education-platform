import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope } from "@/shared/api";
import type {
  DeliveryListFilters,
  DeliveryListResponse,
  DeliveryStatsResponse,
} from "./model/types";

export const adminNotificationsApi = {
  listDeliveries: async (
    filters: DeliveryListFilters & { cursorBefore?: string; cursorId?: string; limit?: number } = {},
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<DeliveryListResponse>>(
      "/admin/notifications/deliveries",
      { params: filters, signal },
    );
    return res.data.result!;
  },

  getStats: async (
    params: { dateFrom?: string; dateTo?: string } = {},
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<DeliveryStatsResponse>>(
      "/admin/notifications/stats",
      { params, signal },
    );
    return res.data.result!;
  },
};

export const adminNotificationQueryOptions = {
  baseKey: "admin-notifications",

  deliveriesKey: (filters?: DeliveryListFilters) =>
    ["admin-notifications", "deliveries", filters ?? {}] as const,

  statsKey: (range?: { dateFrom?: string; dateTo?: string }) =>
    ["admin-notifications", "stats", range ?? {}] as const,

  deliveries: (filters: DeliveryListFilters = {}) =>
    queryOptions({
      queryKey: adminNotificationQueryOptions.deliveriesKey(filters),
      queryFn: ({ signal }) => adminNotificationsApi.listDeliveries({ ...filters, limit: 50 }, { signal }),
    }),

  stats: (range: { dateFrom?: string; dateTo?: string } = {}) =>
    queryOptions({
      queryKey: adminNotificationQueryOptions.statsKey(range),
      queryFn: ({ signal }) => adminNotificationsApi.getStats(range, { signal }),
    }),
};
