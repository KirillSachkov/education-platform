"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRemoveSection(_collectionId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.removeSection,
    onSuccess: async () => {
      toast.success("Секция удалена");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось удалить секцию"));
    },
  });

  return {
    removeSection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
