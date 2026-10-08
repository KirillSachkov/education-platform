import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope } from "@/shared/api";
import type {
  BroadcastNotificationRequest,
  BroadcastNotificationResponse,
  CreateSubscriptionRequest,
  GetNotificationsRequest,
  MarkAllAsReadResponse,
  Notification,
  NotificationListResponse,
  NotificationPreference,
  RegisterPushSubscriptionRequest,
  RemovePushSubscriptionRequest,
  Subscription,
  UnreadCountResponse,
  UpdatePreferencesRequest,
} from "./model/types";

type ItemsResponse<T> = { items: T[] };

export const notificationsApi = {
  getNotifications: async (
    request: GetNotificationsRequest = {},
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<NotificationListResponse>>("/notifications/", {
      params: request,
      // ASP.NET ждёт `?types=10&types=11` (без `[]`), отключаем bracket-индексацию.
      paramsSerializer: { indexes: null },
      signal,
    });
    return res.data.result!;
  },

  getUnreadCount: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<UnreadCountResponse>>("/notifications/unread-count/", {
      signal,
    });
    return res.data.result!;
  },

  markAsRead: async (id: string) => {
    const res = await apiClient.post<Envelope<string>>(`/notifications/${id}/read/`);
    return res.data.result;
  },

  markAllAsRead: async () => {
    const res = await apiClient.post<Envelope<MarkAllAsReadResponse>>("/notifications/read-all/");
    return res.data.result!;
  },

  getPreferences: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<NotificationPreference>>(
      "/notifications/preferences/",
      { signal },
    );
    return res.data.result!;
  },

  updatePreferences: async (request: UpdatePreferencesRequest) => {
    const res = await apiClient.put<Envelope<NotificationPreference>>(
      "/notifications/preferences/",
      request,
    );
    return res.data.result!;
  },

  getSubscriptions: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<ItemsResponse<Subscription>>>(
      "/notifications/subscriptions/",
      { signal },
    );
    return res.data.result!.items;
  },

  subscribe: async (request: CreateSubscriptionRequest) => {
    const res = await apiClient.post<Envelope<Subscription>>(
      "/notifications/subscriptions/",
      request,
    );
    return res.data.result!;
  },

  unsubscribe: async (id: string) => {
    const res = await apiClient.delete<Envelope<string>>(`/notifications/subscriptions/${id}/`);
    return res.data.result;
  },

  broadcast: async (request: BroadcastNotificationRequest) => {
    const res = await apiClient.post<Envelope<BroadcastNotificationResponse>>(
      "/notifications/broadcast/",
      request,
    );
    return res.data.result!;
  },

  // --- Web Push (#342) — register/remove this device's push subscription ---
  registerPushSubscription: async (request: RegisterPushSubscriptionRequest) => {
    const res = await apiClient.post<Envelope<string>>(
      "/notifications/push/subscriptions/",
      request,
    );
    return res.data.result;
  },

  removePushSubscription: async (request: RemovePushSubscriptionRequest) => {
    // DELETE with body: endpoints are long URLs, not path/query-friendly.
    const res = await apiClient.delete<Envelope<string>>("/notifications/push/subscriptions/", {
      data: request,
    });
    return res.data.result;
  },
};

export const notificationQueryOptions = {
  baseKey: "notifications",

  listKey: (filters?: { unreadOnly?: boolean; limit?: number; types?: number[] }) =>
    [notificationQueryOptions.baseKey, "list", filters ?? {}] as const,

  unreadCountKey: () => [notificationQueryOptions.baseKey, "unread-count"] as const,

  preferencesKey: () => [notificationQueryOptions.baseKey, "preferences"] as const,

  subscriptionsKey: () => [notificationQueryOptions.baseKey, "subscriptions"] as const,

  listInfinite: ({
    limit = 20,
    unreadOnly,
    types,
  }: {
    limit?: number;
    unreadOnly?: boolean;
    types?: number[];
  } = {}) =>
    infiniteQueryOptions({
      queryKey: notificationQueryOptions.listKey({ limit, unreadOnly, types }),
      queryFn: ({ pageParam, signal }) =>
        notificationsApi.getNotifications(
          {
            limit,
            unreadOnly,
            types: types as GetNotificationsRequest["types"],
            cursorBefore: pageParam?.cursorBefore,
            cursorId: pageParam?.cursorId,
          },
          { signal },
        ),
      initialPageParam: undefined as { cursorBefore?: string; cursorId?: string } | undefined,
      getNextPageParam: (lastPage) =>
        lastPage.nextCursorBefore && lastPage.nextCursorId
          ? {
              cursorBefore: lastPage.nextCursorBefore,
              cursorId: lastPage.nextCursorId,
            }
          : undefined,
      select: (data) => ({
        items: data.pages.flatMap((page) => page.items) as Notification[],
      }),
    }),

  unreadCount: () =>
    queryOptions({
      queryKey: notificationQueryOptions.unreadCountKey(),
      queryFn: ({ signal }) => notificationsApi.getUnreadCount({ signal }),
      select: (data) => data.count,
      // SSE stream invalidates this key on every notification.created event
      // (see use-notification-stream.ts), so we can safely hold the cache
      // for a long time. Default 60s would re-fetch on every page change.
      staleTime: 5 * 60 * 1000,
    }),

  preferences: () =>
    queryOptions({
      queryKey: notificationQueryOptions.preferencesKey(),
      queryFn: ({ signal }) => notificationsApi.getPreferences({ signal }),
      staleTime: 5 * 60 * 1000,
    }),

  subscriptions: () =>
    queryOptions({
      queryKey: notificationQueryOptions.subscriptionsKey(),
      queryFn: ({ signal }) => notificationsApi.getSubscriptions({ signal }),
      staleTime: 5 * 60 * 1000,
    }),
};
