import { aiReviewApi } from "@/entities/ai-review";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Issue #327 — author submits 👍/👎 feedback на AI iteration.
 * Backend upsert'ит по (iterationId, userId), повторный submit с другим значением
 * меняет existing.
 */
export function useSubmitIterationFeedback() {
  return useMutation({
    mutationFn: aiReviewApi.submitIterationFeedback,
    onSuccess: () => {
      toast.success("Спасибо за фидбэк");
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отправить фидбэк"));
    },
  });
}
