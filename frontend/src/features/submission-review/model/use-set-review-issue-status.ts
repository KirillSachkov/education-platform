import { reviewSubmissionsApi, reviewSubmissionsQueryOptions } from "@/entities/review-submission";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Staff-override (#518) from the author review queue. This changes issue_progress status,
 * not the current review submission state; existing submission actions stay separate.
 */
export function useSetReviewIssueStatus() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: reviewSubmissionsApi.setIssueProgressStatus,
    onSuccess: async () => {
      toast.success("Статус задания изменён");
      await queryClient.invalidateQueries({
        queryKey: [reviewSubmissionsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось изменить статус задания"));
    },
  });

  return {
    setReviewIssueStatus: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
