import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import type { MoveProjectIssueRequest } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useMoveProjectIssue(projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ issueId, request }: { issueId: string; request: MoveProjectIssueRequest }) =>
      projectsApi.moveIssue({ projectId, issueId, request }),
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка перемещения"));
    },
  });

  return {
    moveIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
