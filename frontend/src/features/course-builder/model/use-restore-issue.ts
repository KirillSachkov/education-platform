import { invalidateEducationContent } from "@/entities/course";
import { issuesApi } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRestoreIssue(_projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (issueId: string) => issuesApi.restoreIssue(issueId),
    onSuccess: async () => {
      toast.success("Задача восстановлена");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка восстановления задачи"));
    },
  });

  return {
    restoreIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
