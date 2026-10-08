import { tagsApi, tagsQueryOptions } from "@/entities/tag";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useMergeTags() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.mergeTags,
    onSuccess: async () => {
      toast.success("Теги объединены");
      await queryClient.invalidateQueries({ queryKey: [tagsQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка слияния тегов"));
    },
  });

  return {
    mergeTags: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
