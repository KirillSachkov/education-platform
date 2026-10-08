import axios from "axios";
import type { Session } from "next-auth";
import { fullLogout } from "../auth/full-logout";
import { tokenStore, waitForAuth } from "../auth/token-store";
import { type Envelope, EnvelopeError, ForbiddenError } from "./errors";
import { generateTraceparent } from "./trace-propagation";

let _logoutInProgress = false;

const BASE_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

export const API_ORIGIN = new URL(BASE_URL).origin;

export const apiClient = axios.create({
  baseURL: BASE_URL,
  headers: {
    "Content-Type": "application/json",
  },
});

// Request interceptor: добавляем Bearer token из store (синхронно, без сетевого вызова)
// + W3C Trace Context (traceparent) для корреляции FE-запроса с backend-span'ом в Tempo.
apiClient.interceptors.request.use(async (config) => {
  if (!config.headers["traceparent"]) {
    config.headers["traceparent"] = generateTraceparent();
  }

  if (typeof window !== "undefined") {
    await waitForAuth();

    const { accessToken, error } = tokenStore.getState();

    if (error === "RefreshTokenError" || _logoutInProgress) {
      return Promise.reject(new axios.Cancel("Session expired"));
    }

    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }
  }

  return config;
});

// Response interceptor: проверяем envelope.isError и выбрасываем EnvelopeError
apiClient.interceptors.response.use(
  (response) => {
    const data = response.data as Envelope;

    // Если API вернул isError: true — выбрасываем кастомную ошибку
    if (data?.isError && data.error) {
      throw new EnvelopeError(data.error);
    }

    return response;
  },
  async (error) => {
    // 401 — check if it's a content access denial (not session expiry)
    if (axios.isAxiosError(error) && error.response?.status === 401) {
      const envelope = error.response.data as Envelope | undefined;
      if (envelope?.isError && envelope.error) {
        const isContentAccess = envelope.error.messages?.some((m) =>
          m.code?.startsWith("content.access."),
        );
        if (isContentAccess) {
          return Promise.reject(new EnvelopeError(envelope.error));
        }
      }
    }

    // 401 — access token expired; try refreshing session once before logging out
    // Skip logout flow if user was never authenticated (public page visitor)
    if (
      axios.isAxiosError(error) &&
      error.response?.status === 401 &&
      typeof window !== "undefined" &&
      !(error.config as unknown as Record<string, unknown>)?._isRetry
    ) {
      const { status: authStatus } = tokenStore.getState();

      // User is not authenticated — don't trigger logout, just propagate error
      if (authStatus === "unauthenticated") {
        return Promise.reject(error);
      }

      let session: Session | null = null;
      try {
        const { getSession } = await import("next-auth/react");
        session = await getSession();

        // Transient guard: next-auth fetchData() silently resolves to null on
        // any /api/auth/session network failure (see node_modules/next-auth/
        // lib/client.js:38-41). Retry once with a small delay before treating
        // it as a real logout — otherwise a single flaky request wipes the
        // user's cookie via fullLogout().
        if (!session) {
          await new Promise((r) => setTimeout(r, 500));
          session = await getSession();
        }

        if (session?.accessToken && !session.error) {
          // Если NextAuth отдала тот же мёртвый токен, что только что 401'нул —
          // ретрай бессмысленен и просто дублирует запрос в Network-логе.
          // Бывает когда backend перестал принимать токен, а refresh-flow ещё
          // не сработал.
          const failedAuthHeader = error.config?.headers?.Authorization;
          if (failedAuthHeader === `Bearer ${session.accessToken}`) {
            return Promise.reject(error);
          }

          const retryConfig = Object.assign({}, error.config!, {
            _isRetry: true,
          });
          retryConfig.headers.Authorization = `Bearer ${session.accessToken}`;
          return apiClient(retryConfig);
        }
      } catch {
        // refresh failed — fall through to logout decision
      }

      // Only hard-logout when refresh is definitively dead. A null session
      // after retry is propagated as a plain 401 — the user stays signed in
      // (cookie still valid) and the next request can recover on its own.
      if (session?.error !== "RefreshTokenError") {
        return Promise.reject(error);
      }

      if (!_logoutInProgress) {
        _logoutInProgress = true;
        const fallback = setTimeout(() => { window.location.href = "/login"; }, 3000);
        fullLogout()
          .catch(() => { window.location.href = "/login"; })
          .finally(() => {
            clearTimeout(fallback);
            _logoutInProgress = false;
          });
      }
      return Promise.reject(error);
    }

    // 403 — нет прав доступа
    if (axios.isAxiosError(error) && error.response?.status === 403) {
      return Promise.reject(new ForbiddenError());
    }

    // Network errors или HTTP ошибки (4xx, 5xx)
    if (axios.isAxiosError(error) && error.response?.data) {
      const envelope = error.response.data as Envelope;

      if (envelope?.isError && envelope.error) {
        throw new EnvelopeError(envelope.error);
      }
    }

    // Для остальных ошибок — пробрасываем как есть
    return Promise.reject(error);
  },
);
