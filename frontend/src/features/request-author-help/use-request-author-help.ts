import { apiClient, getErrorMessage, type Envelope } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #383 «Позвать автора» — студент явно подключает автора курса к проверке своего
 * решения (AI-ассистент не справился / нужна живая помощь). По умолчанию автор вне
 * цикла AI-проверки. Идемпотентно на бэке — повторный зов не плодит уведомления.
 */
export function useRequestAuthorHelp(submissionId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (message?: string) => {
      const res = await apiClient.post<Envelope<void>>(
        `/progress/submissions/${submissionId}/request-author-help/`,
        { message: message ?? null },
      );
      return res.data;
    },
    onSuccess: async () => {
      toast.success("Автор позван — он подключится к проверке");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["issue-submission-history"] }),
        queryClient.invalidateQueries({ queryKey: ["course-learning-state"] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось позвать автора"));
    },
  });
}
