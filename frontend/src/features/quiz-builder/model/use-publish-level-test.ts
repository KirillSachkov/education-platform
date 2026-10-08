import { quizzesApi } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateLevelTestEditor } from "./use-create-level-test";

/**
 * Публикация level-test'а (DRAFT → PUBLISHED; бэкенд требует ≥1 вопроса —
 * ошибка показывается через getErrorMessage). Issue #487.
 */
export function usePublishLevelTest() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (quizId: string) => quizzesApi.publishQuiz(quizId),
    onSuccess: async () => {
      toast.success("Тест уровня опубликован");
      await invalidateLevelTestEditor(queryClient);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка публикации теста уровня")),
  });
}
