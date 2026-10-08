import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import type { Envelope } from "@/shared/api";
import { getErrorMessage } from "@/shared/api";
import {
  courseProgressApi,
  courseProgressQueryOptions,
  type CourseLearningStateDto,
} from "@/entities/course-progress";
import { enrollmentQueryOptions } from "@/entities/enrollment";
import { userProgressQueryOptions } from "@/entities/user-progress";

type LearningStateEnvelope = Envelope<CourseLearningStateDto | null>;

/**
 * Снимает user-scoped отметку «материал просмотрен». Зеркало
 * <c>useMarkMaterialViewed</c>: бэкенд удаляет запись из <c>material_views</c> и
 * каскадно откатывает <c>module_item_progress</c> во всех активных enrollment'ах
 * пользователя, где материал есть. XP за просмотр НЕ откатывается.
 */
export function useUnmarkMaterialViewed(courseId?: string) {
  const queryClient = useQueryClient();
  const learningStateKey = courseId ? [courseProgressQueryOptions.baseKey, courseId] : null;

  return useMutation({
    mutationFn: (params: { materialId: string }) =>
      courseProgressApi.unmarkMaterialViewed({ materialId: params.materialId }),
    // Optimistic: patch course-learning-state cache so the toggle button flips
    // back to "Отметить изученным" instantly. Rollback on error.
    onMutate: async ({ materialId }) => {
      if (!learningStateKey) return;
      await queryClient.cancelQueries({ queryKey: learningStateKey });
      const previous = queryClient.getQueryData<LearningStateEnvelope>(learningStateKey);
      const inner = previous?.result;
      if (!previous || !inner) return { previous };

      const nextMaterials = inner.materials.filter((m) => m.materialId !== materialId);

      queryClient.setQueryData<LearningStateEnvelope>(learningStateKey, {
        ...previous,
        result: { ...inner, materials: nextMaterials },
      });
      return { previous };
    },
    onError: (error, _params, context) => {
      if (learningStateKey && context?.previous !== undefined) {
        queryClient.setQueryData(learningStateKey, context.previous);
      }
      toast.error(getErrorMessage(error, "Не удалось снять отметку"));
    },
    onSuccess: async () => {
      toast.success("Отметка о просмотре снята");
      const invalidations = [
        queryClient.invalidateQueries({
          queryKey: [userProgressQueryOptions.baseKey, "material-view-status"],
        }),
      ];
      if (courseId) {
        invalidations.push(
          queryClient.invalidateQueries({
            queryKey: [courseProgressQueryOptions.baseKey, courseId],
          }),
          queryClient.invalidateQueries({
            queryKey: [enrollmentQueryOptions.baseKey, courseId, "my"],
          }),
        );
      }
      await Promise.all(invalidations);
    },
  });
}
