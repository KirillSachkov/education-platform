"use client";

import {
  trainerBookmarksApi,
  trainerSessionQueryOptions,
} from "@/entities/trainer-session";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface RemoveBookmarkVars {
  questionId: string;
}

/**
 * Удаление закладки на вопрос из вкладки «Закладки» хаба (#568). После успеха
 * инвалидируем список закладок (вкладка перерисуется).
 */
export function useRemoveBookmark() {
  const queryClient = useQueryClient();
  return useMutation<void, unknown, RemoveBookmarkVars>({
    mutationFn: ({ questionId }) => trainerBookmarksApi.removeBookmark(questionId),
    onSuccess: async () => {
      toast.success("Закладка удалена");
      await queryClient.invalidateQueries({
        queryKey: trainerSessionQueryOptions.bookmarksKey(),
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось удалить закладку")),
  });
}
