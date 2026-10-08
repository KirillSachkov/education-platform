"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDeleteCollection() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.delete,
    onSuccess: async () => {
      toast.success("Подборка удалена");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось удалить подборку"));
    },
  });

  return {
    deleteCollection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
