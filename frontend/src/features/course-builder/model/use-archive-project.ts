import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useArchiveProject(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (projectId: string) => projectsApi.archiveProject(projectId),
    onSuccess: async () => {
      toast.success("Проект архивирован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка архивации проекта"));
    },
  });

  return {
    archiveProject: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
