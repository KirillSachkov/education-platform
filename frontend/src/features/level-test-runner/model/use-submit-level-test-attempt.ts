"use client";

import {
  levelTestApi,
  levelTestQueryOptions,
  type SubmitLevelTestAttemptRequest,
} from "@/entities/level-test";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

type SubmitLevelTestAttemptInput = SubmitLevelTestAttemptRequest & { viewerScope: string };

/**
 * Сабмит попытки level-test'а. На успех: сидируем кеш результата (страница
 * результата рендерится мгновенно, без refetch-вспышки). Навигацию и
 * owner-scoped fallback делает caller в `onSuccess` своего `mutate`.
 */
export function useSubmitLevelTestAttempt() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ viewerScope: _viewerScope, ...request }: SubmitLevelTestAttemptInput) =>
      levelTestApi.submitAttempt(request),
    onSuccess: (result, { viewerScope }) => {
      queryClient.setQueryData(
        levelTestQueryOptions.attemptResultKey(result.attemptId, viewerScope),
        result,
      );
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка отправки теста")),
  });
}
