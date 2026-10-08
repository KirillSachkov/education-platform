"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useAddSection(_collectionId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.addSection,
    onSuccess: async () => {
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось добавить секцию"));
    },
  });

  return {
    addSection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
