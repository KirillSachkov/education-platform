"use client";

import { trainerBookmarksApi, trainerSessionQueryOptions } from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface ToggleBookmarkVars {
  topicId: string;
  questionId: string;
  /** Текущее состояние закладки — true → удаляем, false → добавляем. */
  isBookmarked: boolean;
}

/**
 * Тоггл закладки на вопрос тренажёра (#568). Add/remove по текущему состоянию;
 * после успеха инвалидируем список закладок. Add повторно → 409 (idempotent) —
 * считаем безобидным, не шумим тостом об ошибке.
 */
export function useToggleBookmark() {
  const queryClient = useQueryClient();
  return useMutation<boolean, unknown, ToggleBookmarkVars>({
    mutationFn: async ({ topicId, questionId, isBookmarked }) => {
      if (isBookmarked) {
        await trainerBookmarksApi.removeBookmark(questionId);
        return false;
      }
      await trainerBookmarksApi.addBookmark({ topicId, questionId });
      return true;
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: trainerSessionQueryOptions.bookmarksKey(),
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось изменить закладку")),
  });
}
