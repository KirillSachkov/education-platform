import { coursesApi, invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDeleteCourse() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.deleteCourse,
    onSuccess: async () => {
      toast.success("Курс удалён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления курса"));
    },
  });

  return {
    deleteCourse: mutation.mutate,
    isPending: mutation.isPending,
  };
}
