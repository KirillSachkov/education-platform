import { aiReviewQueryKeys } from "@/entities/ai-review";
import { apiClient, getErrorMessage, type Envelope } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #383 «Запустить AI принудительно» — автор/админ форсит AI-проверку для submission,
 * у которой ещё нет AiReview (auto-review был выключен / не было VCS installation в
 * момент сабмита). Бэкенд ре-публикует IssueSubmissionAwaitingReview — идемпотентный
 * ARS-handler создаёт+запускает AiReview. После успеха инвалидируем by-submission
 * query, чтобы UI подхватил появившийся AiReview (poll RUNNING → READY).
 *
 * Живёт в submission-review/model (а не отдельной feature-слайсе), потому что вызывается
 * из ai-review-history.tsx внутри той же слайсы — FSD запрещает feature→feature импорт.
 */
export function useRequestAiReview(
  submissionId: string,
  { onStarted }: { onStarted?: () => void } = {},
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post<Envelope<void>>(
        `/progress/submissions/${submissionId}/request-ai-review/`,
      );
      return res.data;
    },
    onSuccess: async () => {
      toast.success("AI-проверка запущена");
      onStarted?.();
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: aiReviewQueryKeys.bySubmission(submissionId),
        }),
        queryClient.invalidateQueries({ queryKey: ["review-submissions"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось запустить AI-проверку"));
    },
  });
}
