import { apiClient, type Envelope } from "@/shared/api";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  AiFeedbackRatingDto,
  CheckAnswerRequest,
  CheckAnswerResponse,
  CreateBookmarkRequest,
  RateAiFeedbackRequest,
  SelfAssessmentDto,
  SelfAssessRequest,
  TrainerBookmarksPage,
  StartLearnSessionRequest,
  StartMockInterviewRequest,
  StartMockSessionRequest,
  StartReviewSessionRequest,
  StartSessionRequest,
  TrainerBookmark,
  TrainerSession,
  TrainerSessionHistoryFilter,
  TrainerSessionHistoryItem,
  TrainerSessionStats,
  TrainerSessionSummary,
} from "./types";
import { isGradingInProgress } from "./lib/grading";

export const trainerSessionsApi = {
  /**
   * Стартовать DRILL-сессию: резолв темы → доступный банк (фримиум) → шафл
   * вопросов, `min(N, доступных)` → снапшот. Возвращает сессию БЕЗ ключа
   * грейдинга. Пустой `mode` = DRILL.
   */
  startSession: async (request: StartSessionRequest): Promise<TrainerSession> => {
    const res = await apiClient.post<Envelope<TrainerSession>>("/trainer/sessions/", request);
    return res.data.result!;
  },

  /**
   * Стартовать MOCK-сессию (симуляция собеса): кросс-тематический пул вопросов
   * по треку, опц. фильтр сложности, информативный таймер. Возвращает сессию
   * БЕЗ ключа грейдинга. Нет доступных вопросов → `trainer.mock.no.questions`.
   */
  startMockSession: async (request: StartMockSessionRequest): Promise<TrainerSession> => {
    const res = await apiClient.post<Envelope<TrainerSession>>(
      "/trainer/mock-sessions/",
      request,
    );
    return res.data.result!;
  },

  /**
   * Стартовать MOCK-сессию из кураторского мок-собеседования (POSITION-подборка,
   * #568): вопросы из тем подборки, информативный таймер. Возвращает сессию БЕЗ
   * ключа грейдинга — тот же раннер, что DRILL/MOCK. Нет доступных вопросов →
   * `trainer.mock.no.questions`.
   */
  startMockInterviewSession: async (
    interviewId: string,
    request: StartMockInterviewRequest,
  ): Promise<TrainerSession> => {
    const res = await apiClient.post<Envelope<TrainerSession>>(
      `/trainer/mock-interviews/${interviewId}/sessions/`,
      request,
    );
    return res.data.result!;
  },

  /**
   * Стартовать LEARN-сессию («обучение по тестам», Ф2): как DRILL, но
   * `Mode=LEARN` + `PER_QUESTION` (мгновенный фидбэк). Каждый `check` апсертит
   * study-state вопроса (formative, без записываемого балла). Нет вопросов →
   * `trainer.learn.no.questions`.
   */
  startLearnSession: async (request: StartLearnSessionRequest): Promise<TrainerSession> => {
    const res = await apiClient.post<Envelope<TrainerSession>>(
      "/trainer/learn-sessions/",
      request,
    );
    return res.data.result!;
  },

  /**
   * Стартовать REVIEW-сессию из произвольного набора вопросов (#568): LEARN-движок
   * (instant feedback) по конкретным `questionIds` — «Доучить» (тест по N ошибкам)
   * и «Пройти тест по закладке». Возвращает сессию БЕЗ ключа грейдинга; драйвится
   * тем же `check`/`complete` флоу. Нет доступных вопросов → `trainer.review.no.questions`.
   */
  startReviewSession: async (request: StartReviewSessionRequest): Promise<TrainerSession> => {
    const res = await apiClient.post<Envelope<TrainerSession>>(
      "/trainer/review-sessions/",
      request,
    );
    return res.data.result!;
  },

  /** Resume/review своей сессии. Отвеченные items раскрывают вердикт + правильный ответ. */
  getSession: async (
    sessionId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerSession> => {
    const res = await apiClient.get<Envelope<TrainerSession>>(`/trainer/sessions/${sessionId}/`, {
      signal,
    });
    return res.data.result!;
  },

  /** История своих сессий (newest-first, до 50; опц. фильтр режим/трек). */
  getMySessions: async (
    filter: TrainerSessionHistoryFilter = {},
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerSessionHistoryItem[]> => {
    const params = new URLSearchParams();
    if (filter.mode) params.set("mode", filter.mode);
    if (filter.trackId) params.set("trackId", filter.trackId);
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerSessionHistoryItem[]>>(
      `/trainer/sessions/my/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /** Разбивка результатов своей сессии: correct/total + срез по сложности и теме. */
  getSessionStats: async (
    sessionId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerSessionStats> => {
    const res = await apiClient.get<Envelope<TrainerSessionStats>>(
      `/trainer/sessions/${sessionId}/stats/`,
      { signal },
    );
    return res.data.result!;
  },

  /**
   * Мгновенная проверка ОДНОГО ответа. Детерминированный грейдинг по снапшоту.
   * Повторная проверка → 409. Возвращает вердикт/балл + правильный ответ/эталон/разбор.
   */
  checkAnswer: async (
    sessionId: string,
    itemId: string,
    request: CheckAnswerRequest,
  ): Promise<CheckAnswerResponse> => {
    const res = await apiClient.post<Envelope<CheckAnswerResponse>>(
      `/trainer/sessions/${sessionId}/answers/${itemId}/check/`,
      request,
    );
    return res.data.result!;
  },

  /**
   * Оценить AI-разбор («Разбор ИИ») отвеченного открытого вопроса 👍/👎 (#691 t7).
   * Апсерт идемпотентен — та же оценка повторно = no-op, противоположная = переключение.
   * Own-data: чужая сессия → 404; не-оценимый item (не открытый / без разбора) → 409.
   */
  rateAiFeedback: async (
    sessionId: string,
    itemId: string,
    request: RateAiFeedbackRequest,
  ): Promise<AiFeedbackRatingDto> => {
    const res = await apiClient.post<Envelope<AiFeedbackRatingDto>>(
      `/trainer/sessions/${sessionId}/answers/${itemId}/feedback-rating/`,
      request,
    );
    return res.data.result!;
  },

  /**
   * Мягкая самооценка «Не уверен» (#691 t8): вопрос своей сессии паркуется в REVIEW (на повтор) —
   * без пометки «неверно» и без движения mastery. Own-data: чужая сессия → 404; неизвестный item → 404.
   * Идемпотентно (повтор держит REVIEW). PATCH — частичное обновление состояния изучения вопроса.
   * Возвращает новый study-status. Невалидный verdict → 400.
   */
  selfAssess: async (
    sessionId: string,
    itemId: string,
    request: SelfAssessRequest,
  ): Promise<SelfAssessmentDto> => {
    const res = await apiClient.patch<Envelope<SelfAssessmentDto>>(
      `/trainer/sessions/${sessionId}/items/${itemId}/self-assess/`,
      request,
    );
    return res.data.result!;
  },

  /** Финализировать сессию: средний балл отвеченных, `status=COMPLETED`. */
  completeSession: async (sessionId: string): Promise<TrainerSessionSummary> => {
    const res = await apiClient.post<Envelope<TrainerSessionSummary>>(
      `/trainer/sessions/${sessionId}/complete/`,
    );
    return res.data.result!;
  },

  /**
   * Отправить голосовой ответ на открытый вопрос (#585): multipart/form-data,
   * поле `audio` — записанный Blob (предпочтительно `audio/webm`). Сервер сам
   * транскрибирует и грейдит — клиент текст НЕ распознаёт. Возвращает
   * `CheckAnswerResponse` (для мока END_OF_SESSION вердикт PENDING, ключ скрыт).
   * Content-Type выставляет axios сам (с boundary) — override per-request на
   * `undefined`. Ошибки: 404 (не владелец), 400 `trainer.answer.not_open_text`,
   * 409 (уже отвечено), 400 `trainer.transcribe.invalid_audio` (пусто / >25MB /
   * не аудио), `trainer.transcribe.failed` (временный сбой STT).
   */
  submitVoiceAnswer: async (
    sessionId: string,
    itemId: string,
    audio: Blob,
  ): Promise<CheckAnswerResponse> => {
    const formData = new FormData();
    formData.append("audio", audio, "answer.webm");
    const res = await apiClient.post<Envelope<CheckAnswerResponse>>(
      `/trainer/sessions/${sessionId}/answers/${itemId}/voice/`,
      formData,
      { headers: { "Content-Type": undefined } },
    );
    return res.data.result!;
  },
};

export const trainerBookmarksApi = {
  /** Страница закладок вызывающего (newest-first) по курсору. */
  getBookmarks: async (
    cursor?: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerBookmarksPage> => {
    const params = new URLSearchParams();
    if (cursor) params.set("cursor", cursor);
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerBookmarksPage>>(
      `/trainer/bookmarks/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? { items: [], nextCursor: null };
  },

  /** Добавить закладку на вопрос (idempotent — повтор → 409). */
  addBookmark: async (request: CreateBookmarkRequest): Promise<TrainerBookmark> => {
    const res = await apiClient.post<Envelope<TrainerBookmark>>("/trainer/bookmarks/", request);
    return res.data.result!;
  },

  /** Удалить закладку (#623 — route по questionId). */
  removeBookmark: async (questionId: string): Promise<void> => {
    await apiClient.delete<Envelope<string>>(`/trainer/bookmarks/${questionId}/`);
  },
};

export const trainerSessionQueryOptions = {
  baseKey: "trainer-sessions",
  bookmarksBaseKey: "trainer-bookmarks",
  historyBaseKey: "trainer-session-history",
  statsBaseKey: "trainer-session-stats",

  sessionKey: (sessionId: string) => [trainerSessionQueryOptions.baseKey, sessionId] as const,
  bookmarksKey: () => [trainerSessionQueryOptions.bookmarksBaseKey] as const,
  historyKey: (filter: TrainerSessionHistoryFilter = {}) =>
    [trainerSessionQueryOptions.historyBaseKey, filter.mode ?? null, filter.trackId ?? null] as const,
  statsKey: (sessionId: string) =>
    [trainerSessionQueryOptions.statsBaseKey, sessionId] as const,

  /**
   * Одна сессия по id (resume/review). Пока AI-грейдинг мока идёт
   * (`gradingStatus` PENDING/GRADING) — поллим раз в 3с; на терминальном статусе
   * (GRADED/FAILED/NOT_REQUIRED) поллинг останавливается (#585).
   */
  sessionOptions: (sessionId: string) =>
    queryOptions({
      queryKey: trainerSessionQueryOptions.sessionKey(sessionId),
      queryFn: ({ signal }) => trainerSessionsApi.getSession(sessionId, { signal }),
      staleTime: 0,
      refetchInterval: (query) =>
        isGradingInProgress(query.state.data?.gradingStatus) ? 3_000 : false,
    }),

  /** Закладки вызывающего — бесконечная подгрузка по курсору. */
  bookmarksInfiniteOptions: () =>
    infiniteQueryOptions({
      queryKey: trainerSessionQueryOptions.bookmarksKey(),
      queryFn: ({ pageParam, signal }) => trainerBookmarksApi.getBookmarks(pageParam, { signal }),
      initialPageParam: undefined as string | undefined,
      getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
      staleTime: 60_000,
    }),

  /**
   * История сессий вызывающего (опц. фильтр режим/трек). Гейтить auth. Пока в выдаче
   * есть мок-симуляция с идущим AI-грейдингом (PENDING/GRADING) — поллим раз в 3с,
   * чтобы строка «ИИ проверяет…» сама сменилась на балл (#568). Иначе поллинг выкл.
   */
  historyOptions: (filter: TrainerSessionHistoryFilter = {}) =>
    queryOptions({
      queryKey: trainerSessionQueryOptions.historyKey(filter),
      queryFn: ({ signal }) => trainerSessionsApi.getMySessions(filter, { signal }),
      staleTime: 30_000,
      refetchInterval: (query) =>
        (query.state.data ?? []).some((session) => isGradingInProgress(session.gradingStatus))
          ? 3_000
          : false,
    }),

  /** Разбивка результатов одной сессии (correct/total + по сложности/теме). */
  statsOptions: (sessionId: string) =>
    queryOptions({
      queryKey: trainerSessionQueryOptions.statsKey(sessionId),
      queryFn: ({ signal }) => trainerSessionsApi.getSessionStats(sessionId, { signal }),
      staleTime: 30_000,
    }),
};
