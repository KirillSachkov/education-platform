import type { OrderStatus } from "@/shared/types/status";

/**
 * Admin-side projections заказа из `/access/admin/orders/*`. В отличие от
 * user-side `MeOrderSummary` содержит UserId + Provider + ExternalProviderRef
 * для саппорта/расследований.
 */
export interface AdminOrderSummary {
  orderId: string;
  userId: string;
  planId: string;
  amountCents: number;
  currency: string;
  status: OrderStatus;
  provider: string | null;
  externalProviderRef: string | null;
  createdAt: string;
  paidAt: string | null;
  failureReason: string | null;
  /** OTel trace_id запроса, создавшего заказ (#443) — связка с логами/трейсами. */
  correlationId: string | null;
}

/** Detail view идентичен Summary; events приклеиваются в GetAdminOrderDetailResponse. */
export type AdminOrderDetail = AdminOrderSummary;

export interface AdminOrderEventDto {
  id: string;
  eventType: string;
  payloadJson: string | null;
  actorUserId: string | null;
  createdAt: string;
  /** OTel trace_id процесса, записавшего событие (#443). */
  correlationId: string | null;
}

export interface GetAdminOrderDetailResponse {
  order: AdminOrderDetail;
  events: AdminOrderEventDto[];
}

export interface ListAdminOrdersResponse {
  items: AdminOrderSummary[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ResyncOrderResponse {
  previousStatus: OrderStatus;
  newStatus: OrderStatus;
}
