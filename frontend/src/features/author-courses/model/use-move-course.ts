import { coursesApi, invalidateEducationContent, type CourseId } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useMoveCourse() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (input: {
      courseId: CourseId;
      afterSortKey?: string;
      beforeSortKey?: string;
    }) =>
      coursesApi.moveCourse({
        courseId: input.courseId,
        request: {
          afterSortKey: input.afterSortKey,
          beforeSortKey: input.beforeSortKey,
        },
      }),
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось переставить курс"));
    },
  });

  return {
    moveCourse: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
