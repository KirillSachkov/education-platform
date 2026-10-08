"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateCollection() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.update,
    onSuccess: async () => {
      toast.success("Подборка обновлена");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось обновить подборку"));
    },
  });

  return {
    updateCollection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
