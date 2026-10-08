import { apiClient, ErrorType, isEnvelopeError, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  CheckQuizQuestionRequest,
  CheckQuizQuestionResultDto,
  CreateQuizRequest,
  MyQuizAttemptsDto,
  MyQuizSummaryDto,
  QuizAttemptResultDto,
  QuizAuthorDto,
  QuizStudentDto,
  SubmitQuizAttemptRequest,
  UpdateQuizRequest,
} from "./types";

export const quizzesApi = {
  /** Студенческая проекция опубликованного квиза. `null` — квиза нет (бэкенд отвечает 404). */
  getMaterialQuiz: async (
    materialId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<QuizStudentDto | null> => {
    try {
      const res = await apiClient.get<Envelope<QuizStudentDto>>(`/materials/${materialId}/quiz/`, {
        signal,
      });
      return res.data.result ?? null;
    } catch (error) {
      if (isEnvelopeError(error) && error.type === ErrorType.NOT_FOUND) {
        return null;
      }
      throw error;
    }
  },

  /**
   * Студенческое standalone-чтение PUBLISHED-квиза (#490, страница ST-16 #495).
   * Answer-stripped, `materialId: null`. Ошибки не глотаются — странице нужны
   * различимые состояния: 404 → «не найден», 401 аноним → CTA «Войти»,
   * 403 → lock-callout.
   */
  getStudentQuiz: async (
    quizId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<QuizStudentDto> => {
    const res = await apiClient.get<Envelope<QuizStudentDto>>(`/quizzes/${quizId}/student/`, {
      signal,
    });
    return res.data.result!;
  },

  /** Полная авторская проекция квиза по id (любой статус, ownership-checked). */
  getAuthorQuiz: async (
    quizId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<QuizAuthorDto> => {
    const res = await apiClient.get<Envelope<QuizAuthorDto>>(`/quizzes/${quizId}/`, { signal });
    return res.data.result!;
  },

  /**
   * Авторская библиотека: ВСЕ квизы caller'а любого статуса и purpose, новые
   * сверху (admin/moderator видят все). LEVEL_TEST фильтруется на фронте —
   * у него своя страница `/author/level-test`. ST-12 #492.
   */
  getMyQuizzes: async ({ signal }: { signal?: AbortSignal } = {}): Promise<MyQuizSummaryDto[]> => {
    const res = await apiClient.get<Envelope<MyQuizSummaryDto[]>>("/quizzes/mine/", { signal });
    return res.data.result ?? [];
  },

  /**
   * Авторский rediscovery level-test'ов — квизы caller'а с purpose=LEVEL_TEST в любом
   * статусе (admin/moderator видят все). Пустой список — теста ещё нет. Issue #487.
   */
  getMyLevelTests: async ({ signal }: { signal?: AbortSignal } = {}): Promise<QuizAuthorDto[]> => {
    const res = await apiClient.get<Envelope<QuizAuthorDto[]>>("/quizzes/level-test/mine/", {
      signal,
    });
    return res.data.result ?? [];
  },

  createQuiz: async (request: CreateQuizRequest) => {
    const res = await apiClient.post<Envelope<string>>("/quizzes/", request);
    return res.data.result!;
  },

  updateQuiz: async (quizId: string, request: UpdateQuizRequest) => {
    const res = await apiClient.put<Envelope<string>>(`/quizzes/${quizId}/`, request);
    return res.data.result!;
  },

  publishQuiz: async (quizId: string) => {
    const res = await apiClient.post<Envelope<string>>(`/quizzes/${quizId}/publish/`);
    return res.data.result!;
  },

  deleteQuiz: async (quizId: string) => {
    await apiClient.delete<Envelope<string>>(`/quizzes/${quizId}/`);
  },
};

export const quizAttemptsApi = {
  submitAttempt: async (quizId: string, request: SubmitQuizAttemptRequest) => {
    const res = await apiClient.post<Envelope<QuizAttemptResultDto>>(
      `/progress/quizzes/${quizId}/attempts/`,
      request,
    );
    return res.data.result!;
  },

  getMyAttempts: async (
    quizId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<MyQuizAttemptsDto> => {
    const res = await apiClient.get<Envelope<MyQuizAttemptsDto>>(
      `/progress/quizzes/${quizId}/attempts/my/`,
      { signal },
    );
    return res.data.result!;
  },

  /**
   * «Проверить ответ» на лету (#556) — раскрывает правильный ответ ОДНОГО вопроса
   * COURSE-квиза после того, как студент его зафиксировал. Попытка не сохраняется;
   * ключ ответов приходит с сервера только для уже проверенного вопроса.
   */
  checkQuestion: async (
    quizId: string,
    questionId: string,
    request: CheckQuizQuestionRequest,
  ): Promise<CheckQuizQuestionResultDto> => {
    const res = await apiClient.post<Envelope<CheckQuizQuestionResultDto>>(
      `/progress/quizzes/${quizId}/questions/${questionId}/check/`,
      request,
    );
    return res.data.result!;
  },
};

export const quizQueryOptions = {
  baseKey: "quizzes",
  attemptsBaseKey: "quiz-attempts",

  materialQuizKey: (materialId: string) =>
    [quizQueryOptions.baseKey, "by-material", materialId] as const,

  studentQuizKey: (quizId: string) => [quizQueryOptions.baseKey, "student", quizId] as const,

  authorQuizKey: (quizId: string) => [quizQueryOptions.baseKey, "author", quizId] as const,

  myQuizzesKey: () => [quizQueryOptions.baseKey, "mine"] as const,

  myLevelTestsKey: () => [quizQueryOptions.baseKey, "level-test", "mine"] as const,

  myAttemptsKey: (quizId: string) => [quizQueryOptions.attemptsBaseKey, quizId, "my"] as const,

  /** Студенческий квиз материала (404 → null — материал без квиза кешируется как null). */
  materialQuizOptions: (materialId: string) =>
    queryOptions({
      queryKey: quizQueryOptions.materialQuizKey(materialId),
      queryFn: ({ signal }) => quizzesApi.getMaterialQuiz(materialId, { signal }),
      staleTime: 60_000,
    }),

  /** Студенческая standalone-проекция (страница `/quizzes/[quizId]`, ST-16 #495). */
  studentQuizOptions: (quizId: string) =>
    queryOptions({
      queryKey: quizQueryOptions.studentQuizKey(quizId),
      queryFn: ({ signal }) => quizzesApi.getStudentQuiz(quizId, { signal }),
      staleTime: 60_000,
    }),

  /** Полная авторская проекция квиза — библиотечный редактор и карточка привязки. */
  authorQuizOptions: (quizId: string) =>
    queryOptions({
      queryKey: quizQueryOptions.authorQuizKey(quizId),
      queryFn: ({ signal }) => quizzesApi.getAuthorQuiz(quizId, { signal }),
      staleTime: 60_000,
    }),

  /** Авторская библиотека квизов (все статусы и purpose, новые сверху). */
  myQuizzesOptions: () =>
    queryOptions({
      queryKey: quizQueryOptions.myQuizzesKey(),
      queryFn: ({ signal }) => quizzesApi.getMyQuizzes({ signal }),
      staleTime: 60_000,
    }),

  /** Level-test'ы caller'а (авторская проекция, любой статус), новые сверху. */
  myLevelTestsOptions: () =>
    queryOptions({
      queryKey: quizQueryOptions.myLevelTestsKey(),
      queryFn: ({ signal }) => quizzesApi.getMyLevelTests({ signal }),
      staleTime: 60_000,
    }),

  /** Попытки текущего юзера по квизу — `{best, last}`, оба null если попыток нет. */
  myAttemptsOptions: (quizId: string) =>
    queryOptions({
      queryKey: quizQueryOptions.myAttemptsKey(quizId),
      queryFn: ({ signal }) => quizAttemptsApi.getMyAttempts(quizId, { signal }),
      staleTime: 60_000,
    }),
};
