import { reviewSubmissionsApi, reviewSubmissionsQueryOptions } from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRequestReviewChanges() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: reviewSubmissionsApi.requestChanges,
    onSuccess: async () => {
      toast.success("Работа отправлена на доработку");
      await queryClient.invalidateQueries({
        queryKey: [reviewSubmissionsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отправить работу на доработку"));
    },
  });

  return {
    requestReviewChanges: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
