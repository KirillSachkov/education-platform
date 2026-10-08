import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRestoreProject(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (projectId: string) => projectsApi.restoreProject(projectId),
    onSuccess: async () => {
      toast.success("Проект восстановлен");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка восстановления проекта"));
    },
  });

  return {
    restoreProject: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
