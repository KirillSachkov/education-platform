import { aiReviewApi, aiReviewQueryKeys } from "./api";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface UseRunAiIterationParams {
  reviewId: string;
  submissionId: string;
}

/**
 * Re-runs an AI iteration on an existing review. Pure AI-review domain
 * operation (single `aiReviewApi.runIteration` call + cache invalidation), so
 * it lives at the entity layer — both the author review-queue (escape hatch:
 * authors can re-check) and any future caller consume it without crossing an
 * FSD feature boundary.
 */
export function useRunAiIteration({ reviewId, submissionId }: UseRunAiIterationParams) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => aiReviewApi.runIteration(reviewId),
    onSuccess: async () => {
      toast.success("AI-проверка запущена");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: aiReviewQueryKeys.bySubmission(submissionId),
        }),
        // Карточка ревью переезжает между табами «Ожидает проверки» / «В проверке»
        // (бэкенд ре-гейтит submission на время прогона). Инвалидируем списки
        // review-submission по literal-префиксу — кросс-слайс import из
        // entities/review-submission запрещён FSD-границами.
        queryClient.invalidateQueries({ queryKey: ["review-submissions"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка запуска AI-проверки"));
    },
  });
}

/**
 * Студенческая доработка после AI-approve с MINOR_ISSUES (#725). Владелец решения
 * запускает повторную AI-проверку своего PR, чтобы закрыть необязательные замечания.
 * Зачёт (COMPLETED) и XP не теряются — новая итерация лишь обновляет вердикт в истории.
 * Живёт на entity-слое рядом с `useRunAiIteration` (чистая AI-review-операция).
 */
export function useStudentRerunReview({ reviewId, submissionId }: UseRunAiIterationParams) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => aiReviewApi.studentRerun(reviewId),
    onSuccess: async () => {
      toast.success("Отправили на повторную проверку");
      await queryClient.invalidateQueries({
        queryKey: aiReviewQueryKeys.bySubmission(submissionId),
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось запустить повторную проверку"));
    },
  });
}

export function useRestartAiReview({ reviewId, submissionId }: UseRunAiIterationParams) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => aiReviewApi.restart(reviewId),
    onSuccess: async () => {
      toast.success("AI-проверка перезапущена");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: aiReviewQueryKeys.bySubmission(submissionId),
        }),
        queryClient.invalidateQueries({ queryKey: ["review-submissions"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка перезапуска AI-проверки"));
    },
  });
}

export function useCancelAiReview({ reviewId, submissionId }: UseRunAiIterationParams) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => aiReviewApi.cancel(reviewId),
    onSuccess: async () => {
      toast.success("AI-проверка остановлена");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: aiReviewQueryKeys.bySubmission(submissionId),
        }),
        queryClient.invalidateQueries({ queryKey: ["review-submissions"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка остановки AI-проверки"));
    },
  });
}

export function useCancelActiveAiReviews() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => aiReviewApi.cancelActive(),
    onSuccess: async (data) => {
      const cancelledCount = data.result?.cancelledCount ?? 0;
      toast.success(
        cancelledCount === 0
          ? "Активных AI-проверок нет"
          : `Остановлено AI-проверок: ${cancelledCount}`,
      );
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [aiReviewQueryKeys.baseKey] }),
        queryClient.invalidateQueries({ queryKey: ["review-submissions"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка остановки AI-проверок"));
    },
  });
}
