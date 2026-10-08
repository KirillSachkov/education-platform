import { videoApi, videoQueryOptions } from "@/entities/video";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

export type UseAttachExternalVideoOptions = {
  entityId?: string | null;
  entityType: string;
  /** Optional draft id — used when the parent entity does not exist yet. */
  draftId?: string | null;
  onSuccess?: (videoId: string) => void;
  onError?: (error: Error) => void;
};

export function useAttachExternalVideo(options: UseAttachExternalVideoOptions) {
  const { entityId, entityType, draftId, onSuccess, onError } = options;
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async (externalVideoId: string) => {
      const usageType =
        entityType === "material"
          ? "material_video"
          : entityType === "lesson"
            ? "lesson_video"
            : "course_video";
      return videoApi.attachExternalVideo({
        externalVideoId,
        usageType,
        targetEntity: entityId ? { type: entityType, id: entityId } : null,
        draftId: draftId ?? null,
      });
    },
    onSuccess: (data) => {
      setError(null);
      if (entityId) {
        queryClient.invalidateQueries({
          queryKey: videoQueryOptions.byEntity(entityId, entityType),
        });
      }
      onSuccess?.(data.assetId);
    },
    onError: (err: Error) => {
      setError(getErrorMessage(err, "Не удалось прикрепить видео"));
      onError?.(err);
    },
  });

  const attach = async (externalVideoId: string) => {
    setError(null);
    return mutation.mutateAsync(externalVideoId);
  };

  const reset = () => {
    setError(null);
    mutation.reset();
  };

  return {
    attach,
    reset,
    isLoading: mutation.isPending,
    isSuccess: mutation.isSuccess,
    error,
    data: mutation.data ?? null,
  };
}
