import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { coursesApi } from "../api";
import { invalidateEducationContent } from "@/shared/lib/invalidate-education-content";

export function useRestoreCourse(_courseId?: string) {
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
