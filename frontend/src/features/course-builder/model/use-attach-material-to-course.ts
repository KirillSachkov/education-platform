"use client";

import { invalidateEducationContent } from "@/entities/course";
import { apiClient, getErrorMessage, type Envelope } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface AttachResponse {
  materialId: string;
  wasAlreadyAttached: boolean;
}

export function useAttachMaterialToCourse(courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async (materialId: string) => {
      const res = await apiClient.post<Envelope<AttachResponse>>(
        `/courses/${courseId}/materials/`,
        { materialId },
      );
      return res.data.result!;
    },
    onSuccess: async (data) => {
      if (data.wasAlreadyAttached) {
        toast.info("Материал уже прикреплён к курсу");
      } else {
        toast.success("Материал добавлен к курсу");
      }
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка добавления материала"));
    },
  });

  return {
    attachMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
