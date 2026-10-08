"use client";

import {
  mockInterviewsApi,
  mockInterviewsQueryOptions,
  type UpdateMockInterviewBody,
} from "@/entities/mock-interview";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface UpdateMockInterviewInput {
  interviewId: string;
  body: UpdateMockInterviewBody;
}

/**
 * Сохранение мок-собеса — PUT replace целиком (title/description +
 * questionsPerSession + курированный набор). Для PUBLISHED изменения видны
 * студентам сразу — инвалидируем и студенческий список. #585.
 */
export function useUpdateMockInterview() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ interviewId, body }: UpdateMockInterviewInput) =>
      mockInterviewsApi.update(interviewId, body),
    onSuccess: async (_id, { interviewId }) => {
      toast.success("Мок-собес сохранён");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: mockInterviewsQueryOptions.manageKey() }),
        queryClient.invalidateQueries({
          queryKey: mockInterviewsQueryOptions.builderKey(interviewId),
        }),
        queryClient.invalidateQueries({ queryKey: mockInterviewsQueryOptions.listKey() }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения мок-собеса")),
  });
}
