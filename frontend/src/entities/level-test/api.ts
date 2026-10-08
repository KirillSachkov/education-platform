import { apiClient, ErrorType, isEnvelopeError, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import {
  isAiGradingPending,
  type ClaimLevelTestAttemptsRequest,
  type ClaimLevelTestAttemptsResponse,
  type LevelTestAttemptResult,
  type LevelTestStudentDto,
  type SubmitLevelTestAttemptRequest,
} from "./types";

/** Интервал поллинга результата, пока AI грейдит развёрнутые ответы. */
export const LEVEL_TEST_AI_POLL_INTERVAL_MS = 4_000;

export const levelTestApi = {
  /** Активный тест уровня. `null` — опубликованного level-test'а нет (бэкенд отвечает 404). */
  getActiveLevelTest: async ({
    signal,
  }: { signal?: AbortSignal } = {}): Promise<LevelTestStudentDto | null> => {
    try {
      const res = await apiClient.get<Envelope<LevelTestStudentDto>>(
        "/quizzes/level-test/active/",
        {
          signal,
        },
      );
      return res.data.result ?? null;
    } catch (error) {
      if (isEnvelopeError(error) && error.type === ErrorType.NOT_FOUND) {
        return null;
      }
      throw error;
    }
  },

  /** Сабмит попытки: аноним получает lead-gated тизер, auth-юзер — полный разбор. */
  submitAttempt: async (
    request: SubmitLevelTestAttemptRequest,
  ): Promise<LevelTestAttemptResult> => {
    const res = await apiClient.post<Envelope<LevelTestAttemptResult>>(
      "/progress/level-test/attempts/",
      request,
    );
    return res.data.result!;
  },

  /** Привязывает ВСЕ неклеймленные попытки анонимного cookie-id к текущему юзеру. */
  claimAttempts: async (
    request: ClaimLevelTestAttemptsRequest,
  ): Promise<ClaimLevelTestAttemptsResponse> => {
    const res = await apiClient.post<Envelope<ClaimLevelTestAttemptsResponse>>(
      "/progress/level-test/attempts/claim/",
      request,
    );
    return res.data.result!;
  },

  /**
   * Последняя попытка текущего юзера (#528): питает блок «твой результат» на
   * лендинге воронки. 404 → null (тест ещё не проходил). Только для auth —
   * caller обязан гейтить `enabled: isAuthenticated` (аноним получит 401).
   */
  getMyLatestAttempt: async ({
    signal,
  }: { signal?: AbortSignal } = {}): Promise<LevelTestAttemptResult | null> => {
    try {
      const res = await apiClient.get<Envelope<LevelTestAttemptResult>>(
        "/progress/level-test/attempts/my-latest/",
        { signal },
      );
      return res.data.result ?? null;
    } catch (error) {
      if (isEnvelopeError(error) && error.type === ErrorType.NOT_FOUND) {
        return null;
      }
      throw error;
    }
  },

  /**
   * Результат попытки: неклеймленная → тизер любому держателю attemptId;
   * клеймленная → полный разбор владельцу (анониму — 401, чужому юзеру — 403).
   */
  getAttemptResult: async (
    attemptId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<LevelTestAttemptResult> => {
    const res = await apiClient.get<Envelope<LevelTestAttemptResult>>(
      `/progress/level-test/attempts/${attemptId}/result/`,
      { signal },
    );
    return res.data.result!;
  },
};

export const levelTestQueryOptions = {
  baseKey: "level-test",

  activeKey: () => [levelTestQueryOptions.baseKey, "active"] as const,

  attemptResultKey: (attemptId: string, viewerScope: string) =>
    [levelTestQueryOptions.baseKey, "attempts", attemptId, "result", viewerScope] as const,

  myLatestAttemptKey: (viewerScope: string) =>
    [levelTestQueryOptions.baseKey, "attempts", "my-latest", viewerScope] as const,

  /** Активный level-test (404 → null кешируется — «тест готовится» заглушка). */
  activeLevelTestOptions: () =>
    queryOptions({
      queryKey: levelTestQueryOptions.activeKey(),
      queryFn: ({ signal }) => levelTestApi.getActiveLevelTest({ signal }),
      staleTime: 5 * 60 * 1000,
    }),

  /** Последний результат текущего юзера; 404 → null. Гейтить `enabled: isAuthenticated`. */
  myLatestAttemptOptions: (viewerScope: string) =>
    queryOptions({
      queryKey: levelTestQueryOptions.myLatestAttemptKey(viewerScope),
      queryFn: ({ signal }) => levelTestApi.getMyLatestAttempt({ signal }),
      staleTime: 60 * 1000,
      retry: false,
      // Пока AI проверяет опены — поллим, чтобы «проверяется…» сам сменился результатом.
      refetchInterval: (query) => {
        const data = query.state.data;
        return data && isAiGradingPending(data.aiGradingStatus)
          ? LEVEL_TEST_AI_POLL_INTERVAL_MS
          : false;
      },
    }),

  /**
   * Результат попытки. Пока `aiGradingStatus` QUEUED|GRADING — поллим каждые
   * ~4с до READY|FAILED|NONE (и тизер, и полный разбор несут статус). 401/403 —
   * терминальные состояния lead-gate'а, не ретраим.
   */
  attemptResultOptions: (attemptId: string, viewerScope: string) =>
    queryOptions({
      queryKey: levelTestQueryOptions.attemptResultKey(attemptId, viewerScope),
      queryFn: ({ signal }) => levelTestApi.getAttemptResult(attemptId, { signal }),
      staleTime: 0,
      retry: false,
      refetchInterval: (query) => {
        const data = query.state.data;
        return data && isAiGradingPending(data.aiGradingStatus)
          ? LEVEL_TEST_AI_POLL_INTERVAL_MS
          : false;
      },
    }),
};
