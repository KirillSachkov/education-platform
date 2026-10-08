import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDetachModuleItem(_courseId: string, moduleId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (referenceId: string) => modulesApi.detachItem({ moduleId, referenceId }),
    onSuccess: async () => {
      toast.success("Урок удалён из модуля");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления"));
    },
  });

  return {
    detachItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
