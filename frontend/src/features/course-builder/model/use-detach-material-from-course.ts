"use client";

import { invalidateEducationContent } from "@/entities/course";
import { apiClient, type Envelope, getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

interface DetachResponse {
  materialId: string;
}

export function useDetachMaterialFromCourse(courseId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async (materialId: string) => {
      const res = await apiClient.delete<Envelope<DetachResponse>>(
        `/courses/${courseId}/materials/${materialId}/`,
      );
      return res.data.result!;
    },
    onSuccess: async () => {
      toast.success("Материал откреплён от курса");
      await invalidateEducationContent(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка открепления материала"));
    },
  });

  return {
    detachMaterial: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
