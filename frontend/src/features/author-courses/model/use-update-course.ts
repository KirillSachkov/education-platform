import { coursesApi, invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateCourse() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.updateCourse,
    onSuccess: async () => {
      toast.success("Курс обновлён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления курса"));
    },
  });

  return {
    updateCourse: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
