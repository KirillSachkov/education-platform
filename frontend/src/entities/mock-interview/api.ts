import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  CreateMockInterviewBody,
  MockInterviewBuilderDto,
  MockInterviewManageItem,
  MockInterviewSummary,
  QuestionBankFilter,
  QuestionBankItem,
  UpdateMockInterviewBody,
} from "./types";

export const mockInterviewsApi = {
  /**
   * Кураторские мок-собеседования (POSITION-ориентированные подборки) — селектор
   * вкладки «Симуляция». Требует auth (`content.view`).
   */
  getList: async ({ signal }: { signal?: AbortSignal } = {}): Promise<MockInterviewSummary[]> => {
    const res = await apiClient.get<Envelope<MockInterviewSummary[]>>("/trainer/mock-interviews/", {
      signal,
    });
    return res.data.result ?? [];
  },

  /**
   * Авторский список мок-собесов (включая DRAFT): метаданные + размеры набора.
   * Admin-only. #585.
   */
  getManageList: async ({
    signal,
  }: { signal?: AbortSignal } = {}): Promise<MockInterviewManageItem[]> => {
    const res = await apiClient.get<Envelope<MockInterviewManageItem[]>>(
      "/trainer/mock-interviews/manage/",
      { signal },
    );
    return res.data.result ?? [];
  },

  /**
   * Детальная карточка мок-собеса для редактора: метаданные + размер подвыборки +
   * курированный набор с резолвнутыми стемами. Admin-only. #585.
   */
  getBuilder: async (
    interviewId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<MockInterviewBuilderDto> => {
    const res = await apiClient.get<Envelope<MockInterviewBuilderDto>>(
      `/trainer/mock-interviews/${interviewId}/builder/`,
      { signal },
    );
    return res.data.result!;
  },

  /**
   * Источник пикера курированного набора: доступные для выбора вопросы (PUBLISHED-
   * темы → их банки → ECS answer-key). Опц. фильтр трек/тема. Admin-only. #585.
   */
  getQuestionBank: async (
    filter: QuestionBankFilter = {},
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<QuestionBankItem[]> => {
    const params = new URLSearchParams();
    if (filter.trackId) params.set("trackId", filter.trackId);
    if (filter.topicId) params.set("topicId", filter.topicId);
    const query = params.toString();
    const res = await apiClient.get<Envelope<QuestionBankItem[]>>(
      `/trainer/question-bank/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /** Создать мок-собес (DRAFT). Slug уникален. Возвращает id. Admin-only. */
  create: async (body: CreateMockInterviewBody): Promise<string> => {
    const res = await apiClient.post<Envelope<{ id: string }>>("/trainer/mock-interviews/", body);
    return res.data.result!.id;
  },

  /**
   * Обновить мок-собес (admin): метаданные + размер подвыборки + полная замена
   * курированного набора. #585.
   */
  update: async (interviewId: string, body: UpdateMockInterviewBody): Promise<string> => {
    const res = await apiClient.put<Envelope<{ id: string }>>(
      `/trainer/mock-interviews/${interviewId}/`,
      body,
    );
    return res.data.result!.id;
  },

  /** Опубликовать мок-собес (требует ≥1 вопроса). Идемпотентно. Admin-only. */
  publish: async (interviewId: string): Promise<string> => {
    const res = await apiClient.post<Envelope<{ id: string }>>(
      `/trainer/mock-interviews/${interviewId}/publish/`,
    );
    return res.data.result!.id;
  },
};

export const mockInterviewsQueryOptions = {
  baseKey: "mock-interviews",
  manageBaseKey: "mock-interviews-manage",
  builderBaseKey: "mock-interview-builder",
  questionBankBaseKey: "trainer-question-bank",

  listKey: () => [mockInterviewsQueryOptions.baseKey] as const,
  manageKey: () => [mockInterviewsQueryOptions.manageBaseKey] as const,
  builderKey: (interviewId: string) =>
    [mockInterviewsQueryOptions.builderBaseKey, interviewId] as const,
  questionBankKey: (filter: QuestionBankFilter = {}) =>
    [
      mockInterviewsQueryOptions.questionBankBaseKey,
      filter.trackId ?? null,
      filter.topicId ?? null,
    ] as const,

  /** Список доступных мок-собеседований (студенческий селектор). */
  listOptions: () =>
    queryOptions({
      queryKey: mockInterviewsQueryOptions.listKey(),
      queryFn: ({ signal }) => mockInterviewsApi.getList({ signal }),
      staleTime: 5 * 60_000,
    }),

  /** Авторский список мок-собесов (включая DRAFT). Admin-only. */
  manageOptions: () =>
    queryOptions({
      queryKey: mockInterviewsQueryOptions.manageKey(),
      queryFn: ({ signal }) => mockInterviewsApi.getManageList({ signal }),
      staleTime: 30_000,
    }),

  /** Детальная карточка мок-собеса для редактора. Admin-only. */
  builderOptions: (interviewId: string) =>
    queryOptions({
      queryKey: mockInterviewsQueryOptions.builderKey(interviewId),
      queryFn: ({ signal }) => mockInterviewsApi.getBuilder(interviewId, { signal }),
      staleTime: 0,
    }),

  /** Пикер банка вопросов (опц. фильтр трек/тема). Admin-only. */
  questionBankOptions: (filter: QuestionBankFilter = {}) =>
    queryOptions({
      queryKey: mockInterviewsQueryOptions.questionBankKey(filter),
      queryFn: ({ signal }) => mockInterviewsApi.getQuestionBank(filter, { signal }),
      staleTime: 60_000,
    }),
};
