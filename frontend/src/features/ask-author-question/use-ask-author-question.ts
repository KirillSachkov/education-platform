import { apiClient, getErrorMessage, type Envelope } from "@/shared/api";
import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/** Состояние «задан ли вопрос автору» по заданию для текущего пользователя (#693). */
export interface AuthorQuestionState {
  /** ISO-время первого вопроса или null, если вопрос не задавался. */
  askedAt: string | null;
}

const authorQuestionKey = (issueId: string) => ["issue-author-question", issueId] as const;

/**
 * #693 — серверное состояние «вопрос автору задан». Питает кнопку: при `askedAt != null`
 * показываем «Вопрос отправлен автору» вместо CTA. Own-data, требует авторизации —
 * вызывать с `enabled` только для залогиненного пользователя.
 */
export const authorQuestionStateQueryOptions = (issueId: string) =>
  queryOptions({
    queryKey: authorQuestionKey(issueId),
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<AuthorQuestionState>>(
        `/progress/issues/${issueId}/author-question/`,
        { signal },
      );
      return res.data;
    },
    select: (data) => data.result!,
  });

/**
 * #693 «Задать вопрос автору» — приватный issue-scoped аналог «Позвать автора» (#383),
 * доступный ДО отправки решения. Идемпотентно на бэке: повторный вопрос не плодит
 * уведомления. После успеха инвалидируем состояние, чтобы кнопка показала «Вопрос отправлен».
 */
export function useAskAuthorQuestion(issueId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (message: string) => {
      const res = await apiClient.post<Envelope<void>>(`/progress/issues/${issueId}/ask-author/`, {
        message,
      });
      return res.data;
    },
    onSuccess: async () => {
      toast.success("Вопрос отправлен автору");
      await queryClient.invalidateQueries({ queryKey: authorQuestionKey(issueId) });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отправить вопрос автору"));
    },
  });
}
