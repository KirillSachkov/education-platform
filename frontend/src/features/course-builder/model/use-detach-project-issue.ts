import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDetachProjectIssue(projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (issueId: string) => projectsApi.detachIssue({ projectId, issueId }),
    onSuccess: async () => {
      toast.success("Задача удалена из проекта");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления"));
    },
  });

  return {
    detachIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
