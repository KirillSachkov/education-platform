import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { OrderStatus } from "@/shared/types/status";
import type {
  GetAdminOrderDetailResponse,
  ListAdminOrdersResponse,
  ResyncOrderResponse,
} from "./types";

export interface AdminOrdersFilter {
  status?: OrderStatus;
  userId?: string;
  planId?: string;
  createdFrom?: string;
  createdTo?: string;
  /** OTel trace_id — точечный поиск заказа по correlation_id (#443). */
  correlationId?: string;
  page?: number;
  pageSize?: number;
}

export const adminOrdersApi = {
  list: async (filter: AdminOrdersFilter = {}, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<ListAdminOrdersResponse>>("/access/admin/orders/", {
      params: filter,
      signal,
    });
    return res.data;
  },

  getDetail: async (orderId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<GetAdminOrderDetailResponse>>(
      `/access/admin/orders/${orderId}`,
      { signal },
    );
    return res.data;
  },

  resync: async (orderId: string) => {
    const res = await apiClient.post<Envelope<ResyncOrderResponse>>(
      `/access/admin/orders/${orderId}/resync`,
      null,
    );
    return res.data;
  },

  revokeGrant: async (orderId: string, reason: string) => {
    const res = await apiClient.post<Envelope<unknown>>(
      `/access/admin/orders/${orderId}/revoke-grant`,
      { reason },
    );
    return res.data;
  },
};

export const adminOrdersQueryOptions = (filter: AdminOrdersFilter = {}) => {
  const status = filter.status ?? null;
  const userId = filter.userId ?? null;
  const planId = filter.planId ?? null;
  const correlationId = filter.correlationId?.trim() ? filter.correlationId.trim() : null;
  const page = filter.page ?? 1;
  const pageSize = filter.pageSize ?? 20;
  return queryOptions({
    queryKey: ["access-admin-orders", { status, userId, planId, correlationId, page, pageSize }],
    queryFn: ({ signal }) =>
      adminOrdersApi.list(
        {
          status: status ?? undefined,
          userId: userId ?? undefined,
          planId: planId ?? undefined,
          correlationId: correlationId ?? undefined,
          page,
          pageSize,
        },
        { signal },
      ),
    select: (data) => data.result!,
    staleTime: 30_000,
  });
};

export const adminOrderDetailQueryOptions = (orderId: string) =>
  queryOptions({
    queryKey: ["access-admin-orders", orderId, "detail"],
    queryFn: ({ signal }) => adminOrdersApi.getDetail(orderId, { signal }),
    select: (data) => data.result!,
    staleTime: 5_000,
  });
