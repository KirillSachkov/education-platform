"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useArchiveCollection() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.archive,
    onSuccess: async () => {
      toast.success("Подборка архивирована");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось архивировать подборку"));
    },
  });

  return {
    archiveCollection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
