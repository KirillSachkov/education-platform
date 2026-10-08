import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateViewPriority(_courseId: string, moduleId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ referenceId, viewPriority }: { referenceId: string; viewPriority: string }) =>
      modulesApi.updateItemViewPriority({ moduleId, referenceId, viewPriority }),
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления приоритета"));
    },
  });

  return {
    updateViewPriority: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
