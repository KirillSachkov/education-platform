"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRemoveItem(_collectionId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.removeItem,
    onSuccess: async () => {
      toast.success("Материал убран из подборки");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось убрать материал"));
    },
  });

  return {
    removeItem: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
