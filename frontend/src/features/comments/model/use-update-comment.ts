import { commentsApi, commentsQueryOptions } from "@/entities/comment";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateComment() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: commentsApi.updateComment,
    onSuccess: async () => {
      toast.success("Комментарий обновлён");
      await queryClient.invalidateQueries({
        queryKey: [commentsQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления комментария"));
    },
  });

  return {
    updateComment: mutation.mutate,
    isError: mutation.isError,
    error: mutation.error,
    isPending: mutation.isPending,
  };
}
