"use client";

import {
  type GetActiveAiJobsResponse,
  materialProcessingQueryOptions,
} from "@/entities/material-processing";
import {
  acquireLeader,
  openChannel,
  subscribeChannel,
} from "@/shared/lib/leader-election";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";

const POLL_LOCK_NAME = "ai-jobs-poll";
const POLL_CHANNEL_NAME = "ai-jobs-snapshot";
const ACTIVE_POLL_INTERVAL_MS = 5_000;
const IDLE_POLL_INTERVAL_MS = 30_000;

type SnapshotMessage = {
  type: "snapshot";
  data: GetActiveAiJobsResponse;
};

/**
 * Leader-only polling AI-jobs трекера с cross-tab snapshot broadcast'ом (#155).
 *
 * Проблема, которую решает: `materialProcessingQueryOptions.activeJobs()` —
 * единственный источник правды для AI-job state, используется и в global
 * `AiJobsTracker` widget и в `video-ai-processing-panel`. До этого хука
 * каждая tab делала свой 5s poll → 3 открытых tabs автора = 3 запроса каждые 5s.
 *
 * Решение: только leader-tab (выбранная через Web Locks API) делает реальный
 * HTTP-запрос; остальные tabs получают snapshots через BroadcastChannel и
 * пишут их в TanStack Query cache через `setQueryData`. Любой
 * `useQuery({...activeJobs()})` в любой tab автоматически рендерит свежие
 * данные (cache subscribers — common pattern).
 *
 * Polling interval — dynamic:
 * - Active jobs > 0 → 5s (нужен plotting прогресса)
 * - Idle (нет active jobs) → 30s (slow-poll чтобы поймать новые jobs от других
 *   admin'ов / другого user-action'а в этой же сессии)
 *
 * Browser fallback: на старых браузерах без Web Locks все tabs ведут себя
 * как leader (per-tab polling, старое поведение).
 *
 * **Где монтировать:** один раз в `AiJobsTracker` widget (он mounted globally
 * в (app)/layout.tsx). Не вызывать в нескольких местах — race на lock не сломает
 * корректность, но создаст лишний overhead.
 */
export function useAiJobsLeaderPoll(enabled: boolean): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!enabled) return;

    const queryKey = materialProcessingQueryOptions.activeJobs().queryKey;

    // Non-leader (и leader тоже — broadcast не доставляет в свою же tab):
    // слушаем snapshot'ы от leader'а и пишем их в cache. Любой
    // useQuery({...activeJobs()}) подхватит новые данные через subscriber'ов.
    const unsubBroadcast = subscribeChannel<SnapshotMessage>(
      POLL_CHANNEL_NAME,
      (msg) => {
        if (msg.type !== "snapshot") return;
        queryClient.setQueryData(queryKey, msg.data);
      },
    );

    // Leader-side: на acquire'е lock'а получаем эксклюзивное право poll'ить.
    // Manual setTimeout вместо TanStack refetchInterval, потому что нам нужен
    // post-fetch hook для broadcast'а — `refetchInterval` его не даёт.
    const channel = openChannel<SnapshotMessage>(POLL_CHANNEL_NAME);

    const releaseLeader = acquireLeader(POLL_LOCK_NAME, () => {
      let cancelled = false;
      let timeoutId: ReturnType<typeof setTimeout> | null = null;

      async function tick(): Promise<void> {
        if (cancelled) return;
        try {
          // TanStack fetchQuery: populates cache → triggers subscribers locally.
          const data = await queryClient.fetchQuery(
            materialProcessingQueryOptions.activeJobs(),
          );
          // Broadcast other tabs (себе не придёт — BroadcastChannel feature).
          channel.post({ type: "snapshot", data });

          if (cancelled) return;
          const interval =
            data.jobs.length > 0
              ? ACTIVE_POLL_INTERVAL_MS
              : IDLE_POLL_INTERVAL_MS;
          timeoutId = setTimeout(tick, interval);
        } catch (error) {
          // Сетевая / auth ошибка — TanStack уже записал её в query.state.
          // Продолжаем с idle-интервалом чтобы не drown'ить backend retry'ями
          // при перезагрузке прокси.
          if (process.env.NODE_ENV === "development") {
            console.warn("[ai-jobs-leader-poll] tick failed:", error);
          }
          if (cancelled) return;
          timeoutId = setTimeout(tick, IDLE_POLL_INTERVAL_MS);
        }
      }

      // Immediate first tick — leader сразу bootstrap'ит snapshot для всех tabs.
      void tick();

      return () => {
        cancelled = true;
        if (timeoutId !== null) clearTimeout(timeoutId);
      };
    });

    // Bootstrap для non-leader: leader, который уже polls, может только что
    // sent'нул snapshot до того как мы subscribed. Делаем разовый prefetch
    // (TanStack дедуплицирует с leader'ом — если leader как раз in-flight,
    // мы переиспользуем его response). Cost: 1 запрос на cold mount tab'а.
    // Без этого non-leader ждал бы до следующего leader tick'а (до 5s).
    void queryClient.prefetchQuery(materialProcessingQueryOptions.activeJobs());

    return () => {
      releaseLeader();
      unsubBroadcast();
      channel.close();
    };
  }, [enabled, queryClient]);
}
