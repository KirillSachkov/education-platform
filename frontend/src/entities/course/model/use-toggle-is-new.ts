import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { getErrorMessage } from "@/shared/api";
import { coursesApi } from "../api";
import { invalidateEducationContent } from "@/shared/lib/invalidate-education-content";

export function useToggleIsNew(courseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (isNew: boolean) => coursesApi.toggleIsNew({ courseId, isNew }),
    onSuccess: async (_, isNew) => {
      toast.success(isNew ? "Курс отмечен как новый" : "Метка «Новый» снята");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления курса"));
    },
  });
}
