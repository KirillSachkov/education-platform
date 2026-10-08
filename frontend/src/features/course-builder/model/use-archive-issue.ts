import { invalidateEducationContent } from "@/entities/course";
import { issuesApi } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useArchiveIssue(_projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (issueId: string) => issuesApi.archiveIssue(issueId),
    onSuccess: async () => {
      toast.success("Задача архивирована");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка архивации задачи"));
    },
  });

  return {
    archiveIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
