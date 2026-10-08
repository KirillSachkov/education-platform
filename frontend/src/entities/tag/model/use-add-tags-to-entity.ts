import { useMutation, useQueryClient } from "@tanstack/react-query";
import { tagsApi, tagsQueryOptions } from "../api";

export function useAddTagsToEntity() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.addTagsToEntity,
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: [tagsQueryOptions.baseKey],
      });
    },
  });

  return {
    addTagsToEntity: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
