import { coursesApi, invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDetachCourseItem(courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (referenceId: string) => coursesApi.detachCourseItem({ courseId, referenceId }),
    onSuccess: async () => {
      toast.success("Элемент удалён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления элемента"));
    },
  });

  return {
    detachCourseItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
