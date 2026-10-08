"use client";

import { useSession } from "next-auth/react";
import { useEffect, useRef } from "react";
import { tokenStore } from "./token-store";

/**
 * Зеркалит NextAuth session в Zustand `tokenStore` для синхронного доступа
 * из axios-интерсептора.
 *
 * Защита от ложного logout'а после фоновой вкладки: внутренний `_getSession`
 * у next-auth (`refetchInterval`-poll или broadcast cross-tab sync) при любой
 * сетевой ошибке `/api/auth/session` молча резолвится в `null` —
 * см. `node_modules/next-auth/lib/client.js:38-41`. На возврате таба это
 * приводит к моментальному `authenticated → unauthenticated`, после чего
 * axios шлёт запросы без `Authorization` и UI показывает «нет доступа».
 *
 * Лечение: если предыдущий статус был `authenticated`, а текущий стал
 * `unauthenticated` БЕЗ явного `session.error` — это transient. Делаем до
 * 3 попыток `update()` с backoff'ом (250 / 1000 / 3000 ms). Только если все
 * три отвалились — принимаем unauthenticated и пушим вниз. Backoff покрывает
 * долгие /api/auth/session reconnect'ы после browser sleep (см. #204).
 */
const RETRY_BACKOFFS_MS = [250, 1000, 3000];

export function TokenSync() {
  const { data: session, status, update } = useSession();
  const wasAuthenticatedRef = useRef(false);
  const retryAttemptRef = useRef(0);
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const sessionError = session?.error;
  const accessToken = session?.accessToken;

  useEffect(() => {
    const isSuspectFlip =
      wasAuthenticatedRef.current &&
      status === "unauthenticated" &&
      !sessionError;

    if (isSuspectFlip && retryAttemptRef.current < RETRY_BACKOFFS_MS.length) {
      const delay = RETRY_BACKOFFS_MS[retryAttemptRef.current]!;
      retryAttemptRef.current += 1;

      // Re-render во время backoff'а (например, при изменении другого поля
      // session) — clearTimeout отменяет старый таймер и перезапускает с
      // тем же attempt-счётчиком, без двойного update().
      if (retryTimerRef.current) clearTimeout(retryTimerRef.current);
      retryTimerRef.current = setTimeout(() => {
        retryTimerRef.current = null;
        void update();
      }, delay);

      return;
    }

    if (status === "authenticated") {
      wasAuthenticatedRef.current = true;
      retryAttemptRef.current = 0;
      if (retryTimerRef.current) {
        clearTimeout(retryTimerRef.current);
        retryTimerRef.current = null;
      }
    } else if (status === "unauthenticated") {
      wasAuthenticatedRef.current = false;
      retryAttemptRef.current = 0;
    }

    tokenStore.getState().setTokenState(accessToken, sessionError, status);
  }, [accessToken, sessionError, status, update]);

  useEffect(() => {
    return () => {
      if (retryTimerRef.current) {
        clearTimeout(retryTimerRef.current);
        retryTimerRef.current = null;
      }
    };
  }, []);

  return null;
}
