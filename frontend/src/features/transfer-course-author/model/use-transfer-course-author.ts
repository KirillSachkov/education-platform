import { coursesApi, invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Передача курса другому автору (#587). После успеха курс уходит из списков прежнего
 * автора и появляется у нового — инвалидируем весь ECS-домен. Шаренные материалы/тесты,
 * оставшиеся у прежнего автора, показываем отдельным info-тостом.
 */
export function useTransferCourseAuthor() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.reassignCourseAuthor,
    onSuccess: async (data) => {
      const result = data.result;
      toast.success("Курс передан автору");

      const skipped =
        (result?.skippedSharedMaterialIds?.length ?? 0) +
        (result?.skippedSharedQuizIds?.length ?? 0);
      if (skipped > 0) {
        toast.info(`${skipped} общих материалов/тестов остались у прежнего автора`);
      }

      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось передать курс"));
    },
  });

  return {
    transferCourseAuthor: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
