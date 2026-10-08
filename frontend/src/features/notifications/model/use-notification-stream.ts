"use client";

import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useStore } from "zustand";
import {
  notificationQueryOptions,
  notificationsApi,
  type UnreadCountResponse,
} from "@/entities/notification";
import { tokenStore } from "@/shared/auth/token-store";
import { acquireLeader, openChannel, subscribeChannel } from "@/shared/lib/leader-election";

const SSE_LOCK_NAME = "sse-notifications";
const SSE_CHANNEL_NAME = "sse-notifications";

/**
 * Сообщения от SSE-leader'а к non-leader tabs.
 * - `invalidate`: список нотификаций изменился — refetch'нуть (list — paginated
 *   infinite-query с разными filter combinations, broadcast'ить snapshot
 *   нерационально, лечится через invalidate + локальный refetch).
 * - `unread-count`: leader уже получил свежее значение — non-leader пишет в
 *   cache через `setQueryData`, badge обновляется мгновенно без HTTP-запроса.
 */
type SseBroadcastMessage =
  | {
      type: "invalidate";
      queryKeys: ReadonlyArray<ReadonlyArray<string>>;
    }
  | {
      type: "unread-count";
      data: UnreadCountResponse;
    };

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";
// SSE endpoint — no trailing slash (streaming endpoint, not a collection)
const STREAM_URL = `${API_BASE}/notifications/stream`;

/**
 * Подписка на NotificationService SSE stream + cross-tab leader election (#154, #155).
 *
 * Multi-tab дедупликация: только одна tab открывает реальный EventSource (leader,
 * выбранный через Web Locks API). Остальные вкладки слушают snapshot/invalidate
 * сообщения через BroadcastChannel и обновляют свои TanStack Query кэши локально.
 *
 * Польза:
 * - 1 SSE connection на user, а не N (backend имеет concurrency limit 3 SSE).
 * - Badge unread-count обновляется на non-leader tabs **мгновенно** (snapshot
 *   broadcast'ится напрямую, без HTTP round-trip'а).
 * - При закрытии leader-tab Web Lock авто-освобождается → одна из waiting tabs
 *   становится leader'ом без manual heartbeat'ов.
 *
 * Транспорт — **нативный браузерный `EventSource`** (а не XHR-based
 * `event-source-polyfill`). XHR-stream завязан на page-load lifecycle мобильного
 * браузера: открытый во время загрузки он держит нативный loading-спиннер, а
 * открытый после `window.load` (попытка #455 это лечить) ломает native
 * pull-to-refresh (#457). Нативный EventSource браузер ведёт как фоновый стрим —
 * он не влияет ни на loader, ни на жесты, поэтому открываем сразу в effect'е без
 * `window.load`-обёрток. Нативный EventSource не умеет слать кастомные headers, так
 * что JWT передаётся query-параметром `access_token` (backend читает его в
 * `JwtBearerEvents.OnMessageReceived` строго для `/notifications/stream`).
 *
 * На старых браузерах без Web Locks (~5%) — fallback на per-tab SSE
 * (старое поведение, ничего не ломается).
 */
export function useNotificationStream() {
  const queryClient = useQueryClient();
  // Subscribe to tokenStore so that the effect re-runs when auth changes.
  const accessToken = useStore(tokenStore, (s) => s.accessToken);
  const authStatus = useStore(tokenStore, (s) => s.status);

  useEffect(() => {
    if (authStatus !== "authenticated" || !accessToken) {
      return;
    }

    // Non-leader tabs: слушают broadcast от leader'а. Этот listener активен в
    // ЛЮБОЙ tab (включая leader — но BroadcastChannel не доставляет в ту же
    // tab откуда posted, так что leader не словит свой же broadcast).
    const unsubBroadcast = subscribeChannel<SseBroadcastMessage>(SSE_CHANNEL_NAME, (msg) => {
      if (msg.type === "invalidate") {
        for (const queryKey of msg.queryKeys) {
          queryClient.invalidateQueries({ queryKey });
        }
        return;
      }
      if (msg.type === "unread-count") {
        // Прямая запись в cache → badge subscriber'ы рендерятся мгновенно.
        queryClient.setQueryData<UnreadCountResponse>(
          notificationQueryOptions.unreadCountKey(),
          msg.data,
        );
      }
    });

    // Leader: попытаться захватить SSE-lock. Если успешно — открываем настоящий
    // EventSource. На abort/unmount — closeLeader освобождает lock и закрывает SSE.
    const releaseLeader = acquireLeader(SSE_LOCK_NAME, () =>
      openSseAsLeader(accessToken, queryClient),
    );

    // PWA на iOS standalone приостанавливает background-вкладку: SSE может
    // оказаться "тихим" после resume. На переход hidden → visible форсим
    // refetch — если SSE жив, это no-op (cache совпадёт); если протух —
    // получим свежее состояние без ожидания EventSource reconnect timer.
    const handleVisibility = () => {
      if (document.visibilityState !== "visible") return;
      queryClient.invalidateQueries({
        queryKey: notificationQueryOptions.unreadCountKey(),
      });
      queryClient.invalidateQueries({
        queryKey: [notificationQueryOptions.baseKey, "list"],
      });
    };
    document.addEventListener("visibilitychange", handleVisibility);

    return () => {
      releaseLeader();
      unsubBroadcast();
      document.removeEventListener("visibilitychange", handleVisibility);
    };
  }, [accessToken, authStatus, queryClient]);
}

/**
 * Реальное открытие EventSource + broadcast обновлений другим вкладкам.
 * Вызывается ТОЛЬКО на leader-tab после acquire'а Web Lock'а.
 *
 * На каждый `notification.created` event:
 * 1. Leader инвалидирует список локально (вернёт refetch при наличии
 *    активного `useNotifications` subscriber'а).
 * 2. Leader fetch'ит свежий unread-count, пишет в свой cache + broadcast'ит
 *    другим tabs (они setQueryData без своего HTTP-запроса).
 * 3. Broadcast invalidate для списка — non-leader-tab refetch'нёт если открыт
 *    notifications-center.
 */
function openSseAsLeader(accessToken: string, queryClient: QueryClient): () => void {
  // Нативный EventSource не шлёт кастомные headers → JWT идёт query-параметром
  // `access_token` (стандартный паттерн ASP.NET Core для SSE/WebSocket). Backend
  // читает его в JwtBearerEvents.OnMessageReceived строго для /notifications/stream;
  // nginx не логирует SSE-локацию, токен короткоживущий (5 мин).
  const url = `${STREAM_URL}?access_token=${encodeURIComponent(accessToken)}`;
  const source = new EventSource(url);

  // Broadcast channel — leader постит сообщения non-leader'ам.
  const channel = openChannel<SseBroadcastMessage>(SSE_CHANNEL_NAME);

  const handleEvent = async () => {
    // Список нотификаций — paginated с разными filter combinations, snapshot
    // broadcastить нерационально → just invalidate. Subscriber'ы (если есть)
    // сделают refetch.
    queryClient.invalidateQueries({
      queryKey: [notificationQueryOptions.baseKey, "list"],
    });
    channel.post({
      type: "invalidate",
      queryKeys: [[notificationQueryOptions.baseKey, "list"]],
    });

    // Unread-count: leader fetch'ит свежее значение (TanStack дедуплицирует
    // с любым in-flight запросом) и broadcast'ит snapshot → non-leader tabs
    // получают новое значение в cache БЕЗ собственного HTTP-запроса.
    try {
      const data = await queryClient.fetchQuery({
        queryKey: notificationQueryOptions.unreadCountKey(),
        queryFn: ({ signal }) => notificationsApi.getUnreadCount({ signal }),
        staleTime: 0, // принудительно свежее значение, не из кэша
      });
      channel.post({ type: "unread-count", data });
    } catch (error) {
      // Сетевая ошибка — fallback на invalidate, non-leader увидит badge
      // изменение когда тот сам fetch'нёт (медленнее, но не сломано).
      if (process.env.NODE_ENV === "development") {
        console.warn("[notifications] unread-count fetch failed:", error);
      }
      channel.post({
        type: "invalidate",
        queryKeys: [notificationQueryOptions.unreadCountKey() as readonly string[]],
      });
    }
  };

  // Стабильная ссылка-обёртка: addEventListener/removeEventListener должны
  // получить один и тот же reference, а `void` гасит floating-promise.
  const onNotificationCreated = () => {
    void handleEvent();
  };

  source.addEventListener("notification.created", onNotificationCreated);

  source.onerror = () => {
    // Нативный EventSource переподключается автоматически (по `retry` директиве
    // сервера). Логируем только в development.
    if (process.env.NODE_ENV === "development") {
      console.warn("[notifications] SSE error — браузер переподключится автоматически");
    }
  };

  return () => {
    source.removeEventListener("notification.created", onNotificationCreated);
    source.close();
    channel.close();
  };
}
