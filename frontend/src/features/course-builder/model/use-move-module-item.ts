import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import type { MoveModuleItemRequest } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useMoveModuleItem(_courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({
      moduleId,
      referenceId,
      request,
    }: {
      moduleId: string;
      referenceId: string;
      request: MoveModuleItemRequest;
    }) => modulesApi.moveItem({ moduleId, referenceId, request }),
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка перемещения"));
    },
  });

  return {
    moveItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
