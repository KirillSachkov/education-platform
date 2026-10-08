import { commentsApi, commentsQueryOptions } from "@/entities/comment";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDeleteComment() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: commentsApi.deleteComment,
    onSuccess: async () => {
      toast.success("Комментарий удалён");
      await queryClient.invalidateQueries({
        queryKey: [commentsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления комментария"));
    },
  });

  return {
    deleteComment: mutation.mutate,
    isError: mutation.isError,
    error: mutation.error,
    isPending: mutation.isPending,
  };
}
