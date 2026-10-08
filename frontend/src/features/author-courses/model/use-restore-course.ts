import { coursesApi, invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRestoreCourse() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.restoreCourse,
    onSuccess: async () => {
      toast.success("Курс восстановлен");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка восстановления курса"));
    },
  });

  return {
    restoreCourse: mutation.mutate,
    isPending: mutation.isPending,
  };
}
