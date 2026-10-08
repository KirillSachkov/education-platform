import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import type { Envelope } from "@/shared/api";
import {
  courseProgressApi,
  courseProgressQueryOptions,
  type CourseLearningStateDto,
} from "@/entities/course-progress";

type LearningStateEnvelope = Envelope<CourseLearningStateDto | null>;

/**
 * Запись «последняя точка пользователя в курсе» — когда ученик открывает урок или задание,
 * фронт фиксирует факт открытия (а не завершения), чтобы кнопка «Продолжить курс» и UI
 * программы знали, на чём ученик остановился. Идемпотентно по паре (user, course):
 * каждое открытие перезаписывает предыдущую запись.
 *
 * Optimistic patch: сразу обновляем `lastPosition` в кэше, чтобы UI не ждал network.
 */
export function useRecordCoursePosition(courseId: string | null | undefined) {
  const queryClient = useQueryClient();
  const learningStateKey = courseId ? [courseProgressQueryOptions.baseKey, courseId] : null;

  return useMutation({
    mutationFn: (params: { entityType: "MATERIAL" | "ISSUE"; entityId: string }) => {
      if (!courseId) throw new Error("courseId is required");
      return courseProgressApi.recordCoursePosition({ courseId, ...params });
    },
    onMutate: async ({ entityType, entityId }) => {
      if (!learningStateKey) return undefined;
      await queryClient.cancelQueries({ queryKey: learningStateKey });
      const previous = queryClient.getQueryData<LearningStateEnvelope>(learningStateKey);
      const inner = previous?.result;
      // Кэша ещё нет — патчить нечего, и rollback-контекст не нужен.
      if (!previous || !inner) return undefined;

      const now = new Date().toISOString();
      queryClient.setQueryData<LearningStateEnvelope>(learningStateKey, {
        ...previous,
        result: { ...inner, lastPosition: { entityType, entityId, openedAt: now } },
      });
      return { previous };
    },
    onError: (_error, _params, context) => {
      // Молчаливо откатываемся — это фоновая телеметрия, тостить не надо.
      if (learningStateKey && context?.previous !== undefined) {
        queryClient.setQueryData(learningStateKey, context.previous);
      }
    },
  });
}

/**
 * Фиксирует открытие урока/задания на mount компонента-страницы. courseId опционален —
 * вне курсового контекста (space-level material) ничего не пишем. Дедупим по паре
 * (entityType, entityId), чтобы re-render'ы внутри страницы не били ручку повторно.
 */
export function useTrackCoursePositionOnMount(
  courseId: string | null | undefined,
  entityType: "MATERIAL" | "ISSUE",
  entityId: string | null | undefined,
) {
  const recordedKeyRef = useRef<string | null>(null);
  const mutation = useRecordCoursePosition(courseId);
  const mutate = mutation.mutate;

  useEffect(() => {
    if (!courseId || !entityId) return;
    const key = `${entityType}:${entityId}`;
    if (recordedKeyRef.current === key) return;
    recordedKeyRef.current = key;
    mutate({ entityType, entityId });
  }, [courseId, entityType, entityId, mutate]);
}
