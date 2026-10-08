import { tagsApi, tagsQueryOptions } from "@/entities/tag";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDeleteTag() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.deleteTag,
    onSuccess: async () => {
      toast.success("Тег удалён");
      await queryClient.invalidateQueries({ queryKey: [tagsQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления тега"));
    },
  });

  return {
    deleteTag: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
