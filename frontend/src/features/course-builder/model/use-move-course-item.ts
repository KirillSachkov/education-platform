import { coursesApi, invalidateEducationContent } from "@/entities/course";
import type { MoveCourseItemRequest } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useMoveCourseItem(courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({
      referenceId,
      request,
    }: {
      referenceId: string;
      request: MoveCourseItemRequest;
    }) => coursesApi.moveCourseItem({ courseId, referenceId, request }),
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка перемещения элемента"));
    },
  });

  return {
    moveCourseItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
