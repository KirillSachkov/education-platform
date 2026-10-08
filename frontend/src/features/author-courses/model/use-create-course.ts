import { coursesApi, invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateCourse() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.createCourse,
    onSuccess: async () => {
      toast.success("Курс создан");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания курса"));
    },
  });

  return {
    createCourse: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
