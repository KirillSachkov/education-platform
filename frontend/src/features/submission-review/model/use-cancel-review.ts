import { reviewSubmissionsApi, reviewSubmissionsQueryOptions } from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #668 «Отказаться от проверки» — автор/админ возвращает взятую в работу (IN_REVIEW) сдачу
 * обратно в «Ожидает проверки» (PENDING), снимая себя как ревьюера. После успеха
 * инвалидируем review-submissions, чтобы сдача переехала во вкладку «Ожидают проверки».
 */
export function useCancelReview() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: reviewSubmissionsApi.cancelReview,
    onSuccess: async () => {
      toast.success("Работа возвращена в «Ожидает проверки»");
      await queryClient.invalidateQueries({
        queryKey: [reviewSubmissionsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отказаться от проверки"));
    },
  });

  return {
    cancelReview: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
