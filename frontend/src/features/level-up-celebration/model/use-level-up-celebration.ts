"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import { NotificationTypes, notificationQueryOptions } from "@/entities/notification";
import { useIsAuthenticated } from "@/shared/auth";

/**
 * Грейс-окно вокруг старта сессии: level-up, созданный чуть РАНЬШЕ монтирования (юзер
 * залевелился и сразу перешёл на другую страницу), всё ещё празднуется; старый непрочитанный
 * level-up из прошлой сессии — нет.
 */
const FRESH_GRACE_MS = 60 * 1000;
const STORAGE_KEY = "plu_celebrated_levelups";
/** Кап на размер localStorage-набора — храним только хвост последних отпразднованных id. */
const MAX_REMEMBERED = 50;

export interface LevelUpCelebrationData {
  notificationId: string;
  newLevel: number;
  totalXp: number;
}

interface LevelUpPayload {
  newLevel?: number;
  totalXp?: number;
}

function loadCelebrated(): string[] {
  if (typeof window === "undefined") return [];
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === "string") : [];
  } catch {
    return [];
  }
}

function persistCelebrated(ids: string[]): void {
  if (typeof window === "undefined") return;
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(ids.slice(-MAX_REMEMBERED)));
  } catch {
    // localStorage недоступен (private mode / quota) — деградируем тихо: модалка
    // может повториться при reload, но это безобидно.
  }
}

/**
 * Выбирает «свежее» непразднованное уведомление о повышении уровня (#555) и даёт dismiss.
 *
 * Источник — обычный список уведомлений (entity-query), отфильтрованный по типу UserLeveledUp +
 * unreadOnly, который инвалидируется SSE-стримом (`[notifications, "list"]`) при каждом
 * `notification.created`. Поэтому level-up, пришедший во время сессии, подхватывается без
 * отдельного канала.
 *
 * Гейты против stale-конфетти: (1) уведомление создано не раньше старта сессии минус грейс —
 * старый непрочитанный level-up при логине не «взрывается»; (2) id не в localStorage-наборе
 * отпразднованных — reload/remount не повторяет модалку.
 */
export function useLevelUpCelebration(): {
  celebration: LevelUpCelebrationData | null;
  dismiss: () => void;
} {
  const isAuthenticated = useIsAuthenticated();
  // Только первая страница (hook никогда не зовёт fetchNextPage): ожидаемый кейс —
  // 0..1 непрочитанный level-up; limit:5 — запас на редкий случай накопления.
  const query = useInfiniteQuery({
    ...notificationQueryOptions.listInfinite({
      limit: 5,
      unreadOnly: true,
      types: [NotificationTypes.UserLeveledUp],
    }),
    enabled: isAuthenticated,
  });
  const items = query.data?.items ?? [];

  // Лениво (один раз) фиксируем старт сессии — чистое значение в render-body, без Date.now().
  const [sessionStartedMs] = useState(() => Date.now());
  const [celebratedIds, setCelebratedIds] = useState<string[]>(loadCelebrated);
  const celebrated = new Set(celebratedIds);

  const fresh = items.find((n) => {
    if (celebrated.has(n.id)) return false;
    const createdMs = new Date(n.createdAt).getTime();
    return Number.isFinite(createdMs) && createdMs >= sessionStartedMs - FRESH_GRACE_MS;
  });

  let celebration: LevelUpCelebrationData | null = null;
  if (fresh) {
    let payload: LevelUpPayload = {};
    try {
      payload = JSON.parse(fresh.payload) as LevelUpPayload;
    } catch {
      payload = {};
    }
    celebration = {
      notificationId: fresh.id,
      newLevel: typeof payload.newLevel === "number" ? payload.newLevel : 0,
      totalXp: typeof payload.totalXp === "number" ? payload.totalXp : 0,
    };
  }

  function dismiss(): void {
    if (!celebration) return;
    const next = [...celebratedIds, celebration.notificationId];
    persistCelebrated(next);
    setCelebratedIds(next);
  }

  return { celebration, dismiss };
}
