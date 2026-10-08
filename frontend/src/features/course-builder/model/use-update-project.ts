import { invalidateEducationContent } from "@/entities/course";
import { projectsApi } from "@/entities/project";
import type { UpdateProjectRequest } from "@/entities/project";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateProject(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ projectId, request }: { projectId: string; request: UpdateProjectRequest }) =>
      projectsApi.updateProject({ projectId, request }),
    onSuccess: async () => {
      toast.success("Проект обновлён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления проекта"));
    },
  });

  return {
    updateProject: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
