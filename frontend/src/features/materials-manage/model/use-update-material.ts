"use client";

import { invalidateEducationContent } from "@/entities/course";
import { materialsApi } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateMaterial() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: materialsApi.updateMaterial,
    onSuccess: async () => {
      toast.success("Материал обновлён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления материала"));
    },
  });

  return {
    updateMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
