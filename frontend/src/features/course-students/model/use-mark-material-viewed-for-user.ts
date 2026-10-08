import {
  courseStudentsApi,
  studentProgressQueryOptions,
} from "@/entities/course-student";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * #398 — staff-override: отметить материал изученным за студента. На success инвалидируем
 * прогресс конкретного студента, чтобы панель перерисовала статус.
 */
export function useMarkMaterialViewedForUser(courseId: string, userId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (materialId: string) =>
      courseStudentsApi.markMaterialViewedForUser({ materialId, userId }),
    onSuccess: async () => {
      toast.success("Материал отмечен изученным");
      await queryClient.invalidateQueries({
        queryKey: studentProgressQueryOptions(courseId, userId).queryKey,
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отметить материал изученным"));
    },
  });

  return {
    markMaterialViewedForUser: mutation.mutateAsync,
    pendingMaterialId: mutation.isPending ? mutation.variables : null,
  };
}
