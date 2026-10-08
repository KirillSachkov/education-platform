import { quizzesApi, type UpdateQuizRequest } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateLevelTestEditor } from "./use-create-level-test";

interface SaveLevelTestInput {
  quizId: string;
  request: UpdateQuizRequest;
}

/**
 * Сохранение level-test'а — PUT replace целиком (title + вопросы + конфиг).
 * Для PUBLISHED-квиза изменения видны студентам сразу — инвалидируем и публичный
 * активный тест. Issue #487.
 */
export function useSaveLevelTest() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ quizId, request }: SaveLevelTestInput) => quizzesApi.updateQuiz(quizId, request),
    onSuccess: async () => {
      toast.success("Тест уровня сохранён");
      await invalidateLevelTestEditor(queryClient);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения теста уровня")),
  });
}
