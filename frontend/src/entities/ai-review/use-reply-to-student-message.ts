import { aiReviewApi, aiReviewQueryKeys } from "./api";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #713 (1b) — автор курса отвечает студенту на его реплику в PR. Pure AI-review
 * domain-операция (единственный `aiReviewApi.replyToStudentMessage` + инвалидация
 * detail-кэша), поэтому живёт на entity-слое — как `useRunAiIteration`. Панель
 * проверки (feature) консьюмит её без пересечения FSD-границы.
 *
 * После успеха инвалидируем `by-submission` detail — рефетч подтянет `answeredAt`
 * и `answerBody` у отвеченного сообщения.
 */
export function useReplyToStudentMessage(submissionId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ messageId, body }: { messageId: string; body: string }) =>
      aiReviewApi.replyToStudentMessage({ messageId, body }),
    onSuccess: async () => {
      toast.success("Ответ отправлен");
      await queryClient.invalidateQueries({
        queryKey: aiReviewQueryKeys.bySubmission(submissionId),
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отправить ответ"));
    },
  });
}
