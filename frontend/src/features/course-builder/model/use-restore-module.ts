import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRestoreModule(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (moduleId: string) => modulesApi.restoreModule(moduleId),
    onSuccess: async () => {
      toast.success("Модуль восстановлен");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка восстановления модуля"));
    },
  });

  return {
    restoreModule: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
