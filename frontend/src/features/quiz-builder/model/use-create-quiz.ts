import { invalidateEducationContent } from "@/entities/course";
import {
  QUIZ_DEFAULT_PASSING_SCORE_PERCENT,
  quizQueryOptions,
  quizzesApi,
} from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Создание standalone-квиза в DRAFT (#489): только title, без вопросов —
 * автор наполняет их в редакторе библиотеки. Используется кнопкой «Создать
 * квиз» в библиотеке и «Создать новый» в блоке привязки на материале.
 */
export function useCreateQuiz() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (title: string) =>
      quizzesApi.createQuiz({
        title,
        questions: [],
        passingScorePercent: QUIZ_DEFAULT_PASSING_SCORE_PERCENT,
      }),
    onSuccess: async () => {
      toast.success("Тест создан");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [quizQueryOptions.baseKey] }),
        invalidateEducationContent(queryClient),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания теста")),
  });
}
