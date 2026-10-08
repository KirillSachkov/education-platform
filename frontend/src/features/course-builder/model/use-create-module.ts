import { coursesApi, invalidateEducationContent } from "@/entities/course";
import type { CreateCourseModuleRequest } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateModule(courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: CreateCourseModuleRequest) =>
      coursesApi.createModule({ courseId, request }),
    onSuccess: async () => {
      toast.success("Модуль создан");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания модуля"));
    },
  });

  return {
    createModule: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
