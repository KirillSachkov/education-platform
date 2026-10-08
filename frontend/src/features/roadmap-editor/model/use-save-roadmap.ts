import { roadmapsApi, roadmapQueryKeys } from "@/entities/roadmap";
import type { SaveCanvasRequest } from "@/entities/roadmap";
import { getErrorMessage } from "@/shared/api/errors";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useSaveRoadmap(roadmapId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: SaveCanvasRequest) => roadmapsApi.saveCanvas({ roadmapId, request }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: [roadmapQueryKeys.base, roadmapId],
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка сохранения роадмапа")),
  });
}
