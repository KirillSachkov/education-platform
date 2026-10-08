import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import type { UpdateModuleRequest } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateModule(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ moduleId, request }: { moduleId: string; request: UpdateModuleRequest }) =>
      modulesApi.updateModule({ moduleId, request }),
    onSuccess: async () => {
      toast.success("Модуль обновлён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления модуля"));
    },
  });

  return {
    updateModule: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
