import { commentsApi, commentsQueryOptions } from "@/entities/comment";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateComment() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: commentsApi.createComment,
    onSuccess: async () => {
      toast.success("Комментарий создан");
      await queryClient.invalidateQueries({
        queryKey: [commentsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка создания комментария"));
    },
  });

  return {
    createComment: mutation.mutate,
    isError: mutation.isError,
    error: mutation.error,
    isPending: mutation.isPending,
  };
}
