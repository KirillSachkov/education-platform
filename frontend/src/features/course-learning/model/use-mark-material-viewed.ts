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
import { trackGrowthEvent } from "@/shared/analytics";

type LearningStateEnvelope = Envelope<CourseLearningStateDto | null>;

/**
 * User-scoped просмотр материала: один факт на пару (user, material), живёт вне
 * привязки к курсу/модулю. Бэкенд каскадит по всем активным enrollments пользователя
 * и закрывает module_item_progress в каждом курсе, где этот материал есть.
 *
 * `courseId` нужен только для invalidate'а локального course-learning-state;
 * сам мутационный запрос принимает только `materialId`.
 */
export function useMarkMaterialViewed(courseId?: string) {
  const queryClient = useQueryClient();
  const learningStateKey = courseId ? [courseProgressQueryOptions.baseKey, courseId] : null;

  return useMutation({
    mutationFn: (params: { materialId: string }) =>
      courseProgressApi.markMaterialViewed({ materialId: params.materialId }),
    // Optimistic: patch the course-learning-state cache so the "Изучено" button,
    // sidebar indicators, and counters update instantly. Rollback on error.
    // Cache stores the raw `Envelope<CourseLearningStateDto>` — the queryOptions'
    // `select` only unwraps for component consumers. Optimistic edits must
    // patch `previous.result.materials`, not `previous.materials`.
    onMutate: async ({ materialId }) => {
      if (!learningStateKey) return;
      await queryClient.cancelQueries({ queryKey: learningStateKey });
      const previous = queryClient.getQueryData<LearningStateEnvelope>(learningStateKey);
      const inner = previous?.result;
      if (!previous || !inner) return { previous };

      const now = new Date().toISOString();
      const alreadyTracked = inner.materials.some((m) => m.materialId === materialId);
      const nextMaterials = alreadyTracked
        ? inner.materials.map((m) =>
            m.materialId === materialId
              ? { ...m, status: "VIEWED" as const, viewedAt: m.viewedAt ?? now }
              : m,
          )
        : [...inner.materials, { materialId, status: "VIEWED" as const, viewedAt: now }];

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
      toast.error(getErrorMessage(error, "Не удалось отметить материал"));
    },
    onSuccess: async (_data, { materialId }) => {
      toast.success("Материал отмечен как просмотренный");
      if (courseId) {
        trackGrowthEvent(
          {
            name: "first_material_completed",
            properties: {
              material_id: materialId,
              course_id: courseId,
            },
          },
          { once: "activation:first-material-completed" },
        );
      }
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
