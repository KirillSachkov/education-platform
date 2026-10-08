"use client";

import { invalidateEducationContent } from "@/entities/course";
import { materialsApi } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useArchiveMaterial() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: materialsApi.archiveMaterial,
    onSuccess: async () => {
      toast.success("Материал архивирован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось архивировать материал"));
    },
  });

  return {
    archiveMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
