import { usersAdminApi, usersQueryOptions } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useCreateUser() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: usersAdminApi.createUser,
    onSuccess: async () => {
      toast.success("Пользователь создан");
      await queryClient.invalidateQueries({
        queryKey: [usersQueryOptions.baseKey],
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания пользователя")),
  });

  return {
    createUser: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
