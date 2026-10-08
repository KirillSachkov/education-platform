import { invalidateEducationContent } from "@/entities/course";
import {
  quizQueryOptions,
  quizzesApi,
  type QuizAccessType,
  type QuizQuestionRequest,
} from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface SaveQuizInput {
  quizId: string;
  title: string;
  passingScorePercent: number;
  accessType: QuizAccessType;
  questions: QuizQuestionRequest[];
}

/**
 * Полное обновление квиза (PUT, replace вопросов целиком + accessType).
 * Инвалидирует все quiz-ключи (библиотека, авторская проекция, студенческий
 * material-quiz) и education-content — title/status квиза денормализованы
 * в course-builder module items и collection items. Issue #471/#494.
 */
export function useSaveQuiz() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ quizId, title, passingScorePercent, accessType, questions }: SaveQuizInput) =>
      quizzesApi.updateQuiz(quizId, { title, passingScorePercent, accessType, questions }),
    onSuccess: async () => {
      toast.success("Тест сохранён");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: [quizQueryOptions.baseKey] }),
        invalidateEducationContent(queryClient),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения теста")),
  });
}
