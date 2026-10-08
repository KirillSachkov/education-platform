import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { videoApi } from "../api";
import { videoQueryOptions } from "./query-options";

export type UseDeleteVideoOptions = {
  onSuccess?: () => void;
};

type DeleteVideoParams = {
  videoId: string;
  deleteFromProvider: boolean;
  entityId?: string;
  entityType?: string;
};

export function useDeleteVideo(options?: UseDeleteVideoOptions) {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async ({ videoId, deleteFromProvider }: DeleteVideoParams) => {
      return await videoApi.deleteVideo(videoId, deleteFromProvider);
    },

    onSuccess: (_, variables) => {
      toast.success("Видео удалено");

      if (variables.entityId && variables.entityType) {
        queryClient.removeQueries({
          queryKey: videoQueryOptions.byEntity(variables.entityId, variables.entityType),
        });
      }

      options?.onSuccess?.();
    },

    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления видео"));
    },
  });

  return {
    deleteVideo: mutation.mutate,
    deleteVideoAsync: mutation.mutateAsync,
    isDeleting: mutation.isPending,
  };
}
