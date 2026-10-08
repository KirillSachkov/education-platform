import { reviewSubmissionsApi, reviewSubmissionsQueryOptions } from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useApproveReview() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: reviewSubmissionsApi.approve,
    onSuccess: async () => {
      toast.success("Работа принята");
      await queryClient.invalidateQueries({
        queryKey: [reviewSubmissionsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось принять работу"));
    },
  });

  return {
    approveReview: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
