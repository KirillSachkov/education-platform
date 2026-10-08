"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateEducationContent } from "@/entities/course";
import { modulesApi } from "@/entities/module";
import { getErrorMessage } from "@/shared/api";

export function useAttachExistingMaterialToModule(moduleId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (materialId: string) =>
      modulesApi.attachMaterial({ moduleId, request: { materialId } }),
    onSuccess: async () => {
      toast.success("Материал добавлен в модуль");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка добавления материала в модуль"));
    },
  });

  return {
    attachMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
