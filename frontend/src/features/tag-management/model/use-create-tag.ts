import { tagsApi, tagsQueryOptions } from "@/entities/tag";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateTag() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.createTag,
    onSuccess: async () => {
      toast.success("Тег создан");
      await queryClient.invalidateQueries({ queryKey: [tagsQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания тега"));
    },
  });

  return {
    createTag: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
