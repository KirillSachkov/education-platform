import { invalidateEducationContent } from "@/entities/course";
import { quizQueryOptions, quizzesApi } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/** Публикация квиза (DRAFT → PUBLISHED, требует ≥1 вопроса на бэкенде). Issue #471. */
export function usePublishQuiz() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (quizId: string) => quizzesApi.publishQuiz(quizId),
    onSuccess: async () => {
      toast.success("Тест опубликован");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [quizQueryOptions.baseKey] }),
        invalidateEducationContent(queryClient),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка публикации теста")),
  });
}
