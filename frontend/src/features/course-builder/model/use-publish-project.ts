import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function usePublishProject(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (projectId: string) => projectsApi.publishProject(projectId),
    onSuccess: async () => {
      toast.success("Проект опубликован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка публикации проекта"));
    },
  });

  return {
    publishProject: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
