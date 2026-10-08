"use client";

import { mockInterviewsApi, mockInterviewsQueryOptions } from "@/entities/mock-interview";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Публикация мок-собеса (DRAFT → PUBLISHED; бэкенд требует ≥1 вопроса —
 * ошибка показывается через getErrorMessage). Идемпотентно. После публикации
 * собес появляется в студенческом селекторе. #585.
 */
export function usePublishMockInterview() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (interviewId: string) => mockInterviewsApi.publish(interviewId),
    onSuccess: async (_id, interviewId) => {
      toast.success("Мок-собес опубликован");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: mockInterviewsQueryOptions.manageKey() }),
        queryClient.invalidateQueries({
          queryKey: mockInterviewsQueryOptions.builderKey(interviewId),
        }),
        queryClient.invalidateQueries({ queryKey: mockInterviewsQueryOptions.listKey() }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка публикации мок-собеса")),
  });
}
