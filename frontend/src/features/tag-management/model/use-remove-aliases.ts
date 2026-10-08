import { tagsApi, tagsQueryOptions } from "@/entities/tag";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRemoveAliases() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.removeAliases,
    onSuccess: async () => {
      toast.success("Алиасы удалены");
      await queryClient.invalidateQueries({ queryKey: [tagsQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления алиасов"));
    },
  });

  return {
    removeAliases: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
