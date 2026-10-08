import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { coursesApi } from "../api";
import { invalidateEducationContent } from "@/shared/lib/invalidate-education-content";

export function usePublishCourse(_courseId?: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: coursesApi.publishCourse,
    onSuccess: async () => {
      toast.success("Курс опубликован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка публикации курса"));
    },
  });

  return {
    publishCourse: mutation.mutate,
    isPending: mutation.isPending,
  };
}
