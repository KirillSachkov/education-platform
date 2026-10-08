"use client";

import { collectionsApi } from "@/entities/collection";
import { invalidateEducationContent } from "@/entities/course";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useSendCollectionToDraft() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: collectionsApi.sendToDraft,
    onSuccess: async () => {
      toast.success("Подборка возвращена в черновик");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось вернуть подборку в черновик"));
    },
  });

  return {
    sendCollectionToDraft: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
