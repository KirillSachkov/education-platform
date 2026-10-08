"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function usePublishCollection() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.publish,
    onSuccess: async () => {
      toast.success("Подборка опубликована");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось опубликовать подборку"));
    },
  });

  return {
    publishCollection: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
