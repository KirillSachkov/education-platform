import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { TrainerQuestionAdmin, TrainerQuestionInput } from "./types";

export const trainerQuestionAdminApi = {
  /**
   * Полные вопросы банка для редактора (с вариантами + isCorrect + эталон +
   * разбор + sortKey), по SortKey. Admin-only (#623).
   */
  getBankQuestions: async (
    bankId: string,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerQuestionAdmin[]> => {
    const res = await apiClient.get<Envelope<TrainerQuestionAdmin[]>>(
      `/trainer/topic-banks/${bankId}/questions/`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /** Создать вопрос в банке. Доменная валидация по типу. Возвращает id. Admin-only. */
  createQuestion: async (bankId: string, body: TrainerQuestionInput): Promise<string> => {
    const res = await apiClient.post<Envelope<{ id: string }>>(
      `/trainer/topic-banks/${bankId}/questions/`,
      body,
    );
    return res.data.result!.id;
  },

  /** Обновить вопрос (полная замена + варианты). BankId/SortKey immutable. Admin-only. */
  updateQuestion: async (questionId: string, body: TrainerQuestionInput): Promise<string> => {
    const res = await apiClient.put<Envelope<{ id: string }>>(
      `/trainer/questions/${questionId}/`,
      body,
    );
    return res.data.result!.id;
  },

  /** Удалить вопрос (варианты каскадятся). Admin-only. */
  deleteQuestion: async (questionId: string): Promise<void> => {
    await apiClient.delete<Envelope<unknown>>(`/trainer/questions/${questionId}/`);
  },
};

export const trainerQuestionAdminQueryOptions = {
  bankQuestionsBaseKey: "trainer-admin-bank-questions",

  bankQuestionsKey: (bankId: string) =>
    [trainerQuestionAdminQueryOptions.bankQuestionsBaseKey, bankId] as const,

  /** Полные вопросы банка для редактора (admin builder-проекция). */
  bankQuestionsOptions: (bankId: string) =>
    queryOptions({
      queryKey: trainerQuestionAdminQueryOptions.bankQuestionsKey(bankId),
      queryFn: ({ signal }) => trainerQuestionAdminApi.getBankQuestions(bankId, { signal }),
      staleTime: 30_000,
    }),
};
