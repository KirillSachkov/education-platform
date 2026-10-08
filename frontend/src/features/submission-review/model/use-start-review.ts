import { reviewSubmissionsApi, reviewSubmissionsQueryOptions } from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useStartReview() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: reviewSubmissionsApi.startReview,
    onSuccess: async () => {
      toast.success("Ревью начато");
      await queryClient.invalidateQueries({
        queryKey: [reviewSubmissionsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось начать ревью"));
    },
  });

  return {
    startReview: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
