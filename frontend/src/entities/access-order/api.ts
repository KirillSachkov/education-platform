import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  CreateOrderRequest,
  CreateOrderResponse,
  GetOrderStatusResponse,
  ListMyOrdersResponse,
  OrderStatus,
} from "./types";

const IDEMPOTENCY_HEADER = "Idempotency-Key";

export const accessOrderApi = {
  /**
   * Создаёт PENDING заказ + дёргает T-Bank Init на сервере → возвращает
   * `{orderId, paymentUrl}`. Caller обязан передать `idempotencyKey` (UUID),
   * чтобы повторный POST с тем же ключом вернул тот же закешированный response
   * вместо создания дубля. Защита от двойного клика и retry'ев на сети.
   */
  createOrder: async (request: CreateOrderRequest, idempotencyKey: string) => {
    const res = await apiClient.post<Envelope<CreateOrderResponse>>("/access/orders/", request, {
      headers: { [IDEMPOTENCY_HEADER]: idempotencyKey },
    });
    return res.data;
  },

  /**
   * Owner-only статус заказа. Используется polling'ом на /payment/success
   * до терминального статуса (PAID/FAILED/REFUNDED) — fallback если webhook
   * от T-Bank ещё не дошёл к моменту redirect'а.
   */
  getOrderStatus: async (orderId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<GetOrderStatusResponse>>(
      `/access/orders/${orderId}/status`,
      { signal },
    );
    return res.data;
  },

  /**
   * Owner-only список заказов текущего юзера. Сортировка по createdAt DESC.
   * Используется страницей `/settings/payments`.
   */
  listMyOrders: async (
    {
      status,
      page,
      pageSize,
    }: { status?: OrderStatus; page?: number; pageSize?: number } = {},
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<ListMyOrdersResponse>>("/access/me/orders/", {
      params: {
        status,
        page,
        pageSize,
      },
      signal,
    });
    return res.data;
  },
};

const TERMINAL_STATUSES: ReadonlyArray<OrderStatus> = ["PAID", "FAILED", "REFUNDED"];

/**
 * Polling-friendly query options. Refetch каждую секунду пока статус не stable.
 * Caller (например, /payment/success page) подаёт `enabled: !!orderId` чтобы не
 * стрелять без orderId.
 */
export const orderStatusQueryOptions = (orderId: string | null) =>
  queryOptions({
    queryKey: ["access-orders", orderId, "status"],
    queryFn: ({ signal }) =>
      accessOrderApi.getOrderStatus(orderId as string, { signal }),
    enabled: !!orderId,
    select: (data) => data.result!,
    refetchInterval: (query) => {
      const status = query.state.data?.result?.status;
      if (status && TERMINAL_STATUSES.includes(status)) return false;
      return 1000;
    },
    // На polling не стоит уверенно ретраить — UI и так refetch'ит каждую секунду.
    retry: 0,
    staleTime: 0,
  });

export const myOrdersQueryOptions = (
  filters: { status?: OrderStatus; page?: number; pageSize?: number } = {},
) => {
  const status = filters.status ?? null;
  const page = filters.page ?? 1;
  const pageSize = filters.pageSize ?? 20;
  return queryOptions({
    queryKey: ["access-orders", "me", { status, page, pageSize }],
    queryFn: ({ signal }) =>
      accessOrderApi.listMyOrders(
        { status: status ?? undefined, page, pageSize },
        { signal },
      ),
    select: (data) => data.result!,
    staleTime: 30_000,
  });
};
