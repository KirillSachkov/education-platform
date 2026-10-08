import { invalidateEducationContent } from "@/entities/course";
import { issuesApi, type UpdateIssueExternalLinksRequest } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateIssueLinks(issueId: string, _projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateIssueExternalLinksRequest) =>
      issuesApi.updateExternalLinks({ issueId, request }),
    onSuccess: async () => {
      toast.success("Ссылки обновлены");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления ссылок"));
    },
  });

  return {
    updateLinks: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
