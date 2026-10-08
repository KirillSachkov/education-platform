import { invalidateEducationContent } from "@/entities/course";
import { issuesApi, type UpdateIssueInternalMaterialsRequest } from "@/entities/issue";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateIssueMaterials(issueId: string, _projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateIssueInternalMaterialsRequest) =>
      issuesApi.updateInternalMaterials({ issueId, request }),
    onSuccess: async () => {
      toast.success("Материалы обновлены");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления материалов"));
    },
  });

  return {
    updateMaterials: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
