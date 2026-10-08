import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import type { CreateProjectIssueRequest } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateIssue(projectId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: CreateProjectIssueRequest) =>
      projectsApi.createIssue({ projectId, request }),
    onSuccess: async () => {
      toast.success("Задача создана");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания задачи"));
    },
  });

  return {
    createIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}

export function useCreateIssueForProject(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({
      projectId,
      request,
    }: {
      projectId: string;
      request: CreateProjectIssueRequest;
    }) => projectsApi.createIssue({ projectId, request }),
    onSuccess: async () => {
      toast.success("Задача создана");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания задачи"));
    },
  });

  return {
    createIssue: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
