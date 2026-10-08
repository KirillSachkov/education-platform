import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { coursesApi } from "../api";
import { invalidateEducationContent } from "@/shared/lib/invalidate-education-content";

export function useArchiveCourse(_courseId?: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.archiveCourse,
    onSuccess: async () => {
      toast.success("Курс архивирован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка архивации курса"));
    },
  });

  return {
    archiveCourse: mutation.mutate,
    isPending: mutation.isPending,
  };
}
