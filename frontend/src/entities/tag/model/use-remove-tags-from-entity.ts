import { useMutation, useQueryClient } from "@tanstack/react-query";
import { tagsApi, tagsQueryOptions } from "../api";

export function useRemoveTagsFromEntity() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.removeTagsFromEntity,
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [tagsQueryOptions.baseKey],
      });
    },
  });

  return {
    removeTagsFromEntity: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
