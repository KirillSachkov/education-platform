"use client";

import { invalidateEducationContent } from "@/entities/course";
import { materialsApi, type MaterialId } from "@/entities/material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export interface PublishMaterialInput {
  materialId: MaterialId;
  notifySubscribers: boolean;
}

export function usePublishMaterial() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ materialId, notifySubscribers }: PublishMaterialInput) =>
      materialsApi.publishMaterial(materialId, { notifySubscribers }),
    onSuccess: async () => {
      toast.success("Материал опубликован");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось опубликовать материал"));
    },
  });

  return {
    publishMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
