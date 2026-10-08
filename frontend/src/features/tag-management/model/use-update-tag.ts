import { tagsApi, tagsQueryOptions } from "@/entities/tag";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateTag() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: tagsApi.updateTag,
    onSuccess: async () => {
      toast.success("Тег обновлён");
      await queryClient.invalidateQueries({ queryKey: [tagsQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка переименования тега"));
    },
  });

  return {
    updateTag: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
