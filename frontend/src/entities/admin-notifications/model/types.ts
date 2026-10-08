/**
 * DTOs для admin delivery audit (NotificationService.Contracts.Admin.Dtos).
 * Channel bitmask: InApp=1, Telegram=2, Email=4. Status: Pending=0, Delivered=1, Failed=2, Skipped=3.
 */
export interface DeliveryListItem {
  deliveryId: string;
  notificationId: string;
  recipientUserId: string;
  type: number;
  templateId: string;
  channel: number;
  status: number;
  providerMessageId: string | null;
  errorCode: string | null;
  errorDetail: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface DeliveryListResponse {
  items: DeliveryListItem[];
  nextCursorBefore: string | null;
  nextCursorId: string | null;
}

export interface ChannelStatusBucket {
  channel: number;
  status: number;
  count: number;
}

export interface TypeBucket {
  type: number;
  count: number;
}

export interface FailureReason {
  errorCode: string;
  count: number;
}

export interface DeliveryStatsResponse {
  perChannel: ChannelStatusBucket[];
  perType: TypeBucket[];
  topFailures: FailureReason[];
}

export interface DeliveryListFilters {
  status?: number;
  channel?: number;
  recipientUserId?: string;
  dateFrom?: string;
  dateTo?: string;
}

/** Маппинги human-readable для UI. */
export const DeliveryChannelLabels: Record<number, string> = {
  1: "InApp",
  2: "Telegram",
  4: "Email",
};

export const DeliveryStatusLabels: Record<number, string> = {
  0: "Pending",
  1: "Доставлено",
  2: "Ошибка",
  3: "Пропущено",
};

export const DeliveryStatusColors: Record<number, "default" | "secondary" | "destructive"> = {
  0: "secondary",
  1: "default",
  2: "destructive",
  3: "secondary",
};
