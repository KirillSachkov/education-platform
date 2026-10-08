"use client";

import { trainerQuestionsQueryOptions } from "@/entities/trainer-question";
import { trainerBookmarksApi, trainerSessionQueryOptions } from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface ToggleVars {
  topicId: string;
  questionId: string;
  /** Текущее состояние — true → удаляем, false → добавляем. */
  isBookmarked: boolean;
}

/**
 * Тоггл закладки на вопрос прямо из списка «Изучение» (#568 design pass 2).
 * Дублирует `bookmark-question` / `use-study-card-bookmark`, но ходит в
 * `trainerBookmarksApi` напрямую — feature не может импортировать соседнюю feature
 * (FSD), переиспользуем entity-API. Инвалидирует список (поле `isBookmarked`) и
 * вкладку «Закладки». Add повторно → 409 (idempotent), не шумим тостом.
 */
export function useToggleQuestionBookmark() {
  const queryClient = useQueryClient();
  return useMutation<boolean, unknown, ToggleVars>({
    mutationFn: async ({ topicId, questionId, isBookmarked }) => {
      if (isBookmarked) {
        await trainerBookmarksApi.removeBookmark(questionId);
        return false;
      }
      await trainerBookmarksApi.addBookmark({ topicId, questionId });
      return true;
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: trainerSessionQueryOptions.bookmarksKey(),
        }),
        queryClient.invalidateQueries({
          queryKey: [trainerQuestionsQueryOptions.listBaseKey],
        }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось изменить закладку")),
  });
}
