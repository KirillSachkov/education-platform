import { coursesApi, invalidateEducationContent } from "@/entities/course";
import type { CreateCourseProjectRequest } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateProject(courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: CreateCourseProjectRequest) =>
      coursesApi.createProject({ courseId, request }),
    onSuccess: async () => {
      toast.success("Проект создан");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания проекта"));
    },
  });

  return {
    createProject: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
