import { invalidateEducationContent } from "@/entities/course";
import { issuesApi, type UpdateIssueRequest } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateIssue(issueId: string, _projectId: string, _courseId?: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateIssueRequest) => issuesApi.updateIssue({ issueId, request }),
    onSuccess: async () => {
      toast.success("Задача обновлена");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления"));
    },
  });

  return {
    updateIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
