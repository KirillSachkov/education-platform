import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useArchiveModule(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (moduleId: string) => modulesApi.archiveModule(moduleId),
    onSuccess: async () => {
      toast.success("Модуль архивирован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка архивации модуля"));
    },
  });

  return {
    archiveModule: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
