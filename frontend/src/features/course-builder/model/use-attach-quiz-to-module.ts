"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { quizQueryOptions } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";

/**
 * Привязка квиза к модулю (ST-12 #492, зеркало useAttachExistingMaterialToModule).
 * Инвалидируем и quiz-ключи: courseCount в библиотеке растёт от derived-привязки
 * `course_quizzes`.
 */
export function useAttachQuizToModule(moduleId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (quizId: string) => modulesApi.attachQuiz({ moduleId, request: { quizId } }),
    onSuccess: async () => {
      toast.success("Тест добавлен в модуль");
      await Promise.all([
        invalidateEducationContent(queryClient),
        queryClient.invalidateQueries({ queryKey: [quizQueryOptions.baseKey] }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка добавления теста в модуль"));
    },
  });

  return {
    attachQuiz: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
