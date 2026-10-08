"use client";

import { invalidateEducationContent } from "@/entities/course";
import { materialsApi } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateMaterial() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: materialsApi.createMaterial,
    onSuccess: async () => {
      toast.success("Материал создан");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания материала"));
    },
  });

  return {
    createMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
