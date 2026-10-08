import { invalidateEducationContent } from "@/entities/course";
import { quizQueryOptions, quizzesApi } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Hard-delete квиза. Бэкенд каскадит ссылки в одной транзакции: `materials.quiz_id`,
 * `course_quizzes`, `module_items`, `collection_items` (#489-#493) — поэтому
 * инвалидируем и education-content (builder, подборки, материалы). Issue #471.
 */
export function useDeleteQuiz() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (quizId: string) => quizzesApi.deleteQuiz(quizId),
    onSuccess: async () => {
      toast.success("Тест удалён");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [quizQueryOptions.baseKey] }),
        invalidateEducationContent(queryClient),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка удаления теста")),
  });
}
