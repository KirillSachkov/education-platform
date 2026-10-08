import { issuesApi, issuesQueryOptions } from "@/entities/issue";
import type { UpdateReviewSpecRequest } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateReviewSpec(issueId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateReviewSpecRequest) =>
      issuesApi.updateReviewSpec({ issueId, request }),
    onSuccess: async () => {
      toast.success("Настройки AI-проверки сохранены");
      await queryClient.invalidateQueries({
        queryKey: [issuesQueryOptions.baseKey, issueId, "review-spec"],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка сохранения настроек AI"));
    },
  });
}
