"use client";

import { levelTestApi, levelTestQueryOptions } from "@/entities/level-test";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface ClaimLevelTestAttemptsInput {
  anonymousId: string;
  attemptId: string;
  viewerScope: string;
}

/**
 * Клейм анонимных попыток после возврата с логина: биндит ВСЕ попытки
 * cookie-id к юзеру, затем инвалидирует+рефетчит результат — lead-gate
 * раскрывается с тизера до полного разбора.
 */
export function useClaimLevelTestAttempts() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ anonymousId }: ClaimLevelTestAttemptsInput) =>
      levelTestApi.claimAttempts({ anonymousId }),
    onSuccess: async (_result, { attemptId, viewerScope }) => {
      await queryClient.invalidateQueries({
        queryKey: levelTestQueryOptions.attemptResultKey(attemptId, viewerScope),
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка привязки результата")),
  });
}
