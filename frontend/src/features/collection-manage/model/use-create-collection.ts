"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateCollection() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.create,
    onSuccess: async () => {
      toast.success("Подборка создана");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось создать подборку"));
    },
  });

  return {
    createCollection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
