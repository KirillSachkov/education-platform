import {
  reviewSubmissionsApi,
  reviewSubmissionsQueryOptions,
} from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #383 — ручная приёмка работы автором/админом («Отметить выполненным»). Форс-аппрув
 * попытки из любого статуса проверки (PENDING / IN_REVIEW / CHANGES_REQUESTED) — AI
 * ассистент, последнее слово за автором.
 */
export function useMarkCompleteReview() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: reviewSubmissionsApi.markComplete,
    onSuccess: async () => {
      toast.success("Работа отмечена выполненной");
      await queryClient.invalidateQueries({
        queryKey: [reviewSubmissionsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отметить работу выполненной"));
    },
  });

  return {
    markCompleteReview: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
