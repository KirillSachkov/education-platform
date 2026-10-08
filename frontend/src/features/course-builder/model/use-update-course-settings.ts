import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { coursesApi, invalidateEducationContent } from "@/entities/course";

import type { UpdateCourseRequest } from "@/entities/course";

/**
 * Тонкая обёртка над `coursesApi.updateCourse` для course-builder экранов.
 *
 * PATCH-семантика: принимаемый объект напрямую отправляется в бэк,
 * передавай только меняющиеся поля. Бэк не трогает то, что не пришло —
 * echo-back текущих значений больше не нужен.
 */
export function useUpdateCourseSettings(courseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateCourseRequest) =>
      coursesApi.updateCourse({
        courseId,
        request: data,
      }),
    onSuccess: async () => {
      toast.success("Настройки сохранены");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка сохранения"));
    },
  });
}
