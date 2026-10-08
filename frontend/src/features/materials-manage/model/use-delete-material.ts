"use client";

import { invalidateEducationContent } from "@/entities/course";
import { materialsApi } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDeleteMaterial() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: materialsApi.deleteMaterial,
    onSuccess: async () => {
      toast.success("Материал удалён");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось удалить материал"));
    },
  });

  return {
    deleteMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
