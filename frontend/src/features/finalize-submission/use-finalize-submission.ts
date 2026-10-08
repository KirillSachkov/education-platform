import { aiReviewApi, aiReviewQueryKeys } from "@/entities/ai-review";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Phase 8 (#15) — студент подтверждает что submission готова к ручному ревью.
 * Снимает ReadyForHumanReview гейт после прогона AI iteration'ов.
 */
export function useFinalizeSubmission(submissionId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => aiReviewApi.finalizeSubmission(submissionId),
    onSuccess: async () => {
      toast.success("Решение отправлено автору на ревью");
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: aiReviewQueryKeys.bySubmission(submissionId),
        }),
        queryClient.invalidateQueries({ queryKey: ["course-learning-state"] }),
        queryClient.invalidateQueries({ queryKey: ["issue-submission-history"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка отправки на ревью"));
    },
  });
}
