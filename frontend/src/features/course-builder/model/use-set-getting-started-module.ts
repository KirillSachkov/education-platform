import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { coursesApi, invalidateEducationContent } from "@/entities/course";

export function useSetGettingStartedModule(courseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (gettingStartedModuleId: string | null) =>
      coursesApi.updateCourse({
        courseId,
        request: { gettingStartedModuleId },
      }),
    onSuccess: async () => {
      toast.success("Стартовый модуль обновлён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления"));
    },
  });
}
