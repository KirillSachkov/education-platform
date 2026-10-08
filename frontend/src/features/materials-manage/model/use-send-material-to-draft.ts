"use client";

import { invalidateEducationContent } from "@/entities/course";
import { materialsApi } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useSendMaterialToDraft() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: materialsApi.sendMaterialToDraft,
    onSuccess: async () => {
      toast.success("Материал возвращён в черновик");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось изменить статус материала"));
    },
  });

  return {
    sendMaterialToDraft: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
