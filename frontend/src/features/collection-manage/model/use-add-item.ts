"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useAddItem(_collectionId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.addItem,
    onSuccess: async () => {
      toast.success("Добавлено в подборку");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось добавить элемент"));
    },
  });

  return {
    addItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
