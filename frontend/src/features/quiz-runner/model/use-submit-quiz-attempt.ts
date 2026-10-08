import {
  QUIZ_CHANGED_RELOAD_CODE,
  quizAttemptsApi,
  quizQueryOptions,
  type SubmitQuizAttemptRequest,
} from "@/entities/quiz";
import { courseProgressQueryOptions } from "@/entities/course-progress";
import { getErrorMessage, isEnvelopeError } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * true, если сабмит завалился из-за того, что автор отредактировал тест во время
 * прохождения (409 `quiz.changed.reload`). В этом случае разбор НЕ рисуется — панель
 * показывает inline-сообщение с кнопкой перезагрузки (#556).
 */
export function isQuizChangedError(error: unknown): boolean {
  return isEnvelopeError(error) && error.messages.some((m) => m.code === QUIZ_CHANGED_RELOAD_CODE);
}

/**
 * Сабмит попытки квиза с автогрейдингом. Без success-toast'а — результат сразу
 * рисуется блоком разбора. Кеш `{best, last}` инвалидируется, чтобы summary-карточка
 * показала свежую попытку после «Пройти ещё раз». Issue #471.
 *
 * Также инвалидируется сводка `my-summary` (#578) — её показывает курсовая вкладка
 * «Тесты» (лучший/последний балл + попытки), иначе после пересдачи строка теста
 * остаётся со старыми цифрами (TTL 60s, SPA-навигация не ремаунтит → нет refetch).
 */
export function useSubmitQuizAttempt(quizId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: SubmitQuizAttemptRequest) =>
      quizAttemptsApi.submitAttempt(quizId, request),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: quizQueryOptions.myAttemptsKey(quizId) }),
        queryClient.invalidateQueries({ queryKey: courseProgressQueryOptions.mySummaryKey }),
      ]);
    },
    onError: (error) => {
      // Quiz-changed обрабатывается инлайн-баннером в панели — generic-toast не нужен.
      if (isQuizChangedError(error)) return;
      toast.error(getErrorMessage(error, "Ошибка отправки ответов"));
    },
  });
}
