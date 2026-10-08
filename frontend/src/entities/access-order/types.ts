// OrderStatus переехал в shared/types/status.ts (#124-fix) — нужен для
// entities/access-admin-order без cross-entity import. Re-export для
// backward-compat existing imports `from "@/entities/access-order"`.
export type { OrderStatus } from "@/shared/types/status";
import type { OrderStatus } from "@/shared/types/status";

/**
 * Request body для `POST /access/orders/`. Frontend шлёт только planId — цена
 * snapshot'ится из `plan.priceCents` на сервере (защита от tampering). Idempotency-Key
 * передаётся отдельным header'ом, не в body.
 */
export interface CreateOrderRequest {
  planId: string;
}

export interface CreateOrderResponse {
  orderId: string;
  paymentUrl: string;
}

export interface GetOrderStatusResponse {
  orderId: string;
  status: OrderStatus;
  paidAt: string | null;
  failureReason: string | null;
}

/**
 * User-side projection заказа из `GET /access/me/orders/`. Не содержит
 * provider-internal полей (Provider, ExternalProviderRef).
 */
export interface MeOrderSummary {
  orderId: string;
  planId: string;
  /** В копейках. JS number safely до 2^53 — long-tier цены влезают. */
  amountCents: number;
  currency: string;
  status: OrderStatus;
  createdAt: string;
  paidAt: string | null;
  failureReason: string | null;
  /** Display name плана (#512) — фронту не нужен полный каталог планов. Null если план удалён. */
  planTitle: string | null;
}

export interface ListMyOrdersResponse {
  items: MeOrderSummary[];
  total: number;
  page: number;
  pageSize: number;
}
