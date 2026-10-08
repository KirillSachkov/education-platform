import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function usePublishModule(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (moduleId: string) => modulesApi.publishModule(moduleId),
    onSuccess: async () => {
      toast.success("Модуль опубликован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка публикации модуля"));
    },
  });

  return {
    publishModule: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
