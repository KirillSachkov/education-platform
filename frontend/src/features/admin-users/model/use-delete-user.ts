import { usersAdminApi, usersQueryOptions } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useDeleteUser() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: usersAdminApi.deleteUser,
    onSuccess: async () => {
      toast.success("Пользователь удалён");
      await queryClient.invalidateQueries({
        queryKey: [usersQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка удаления пользователя"));
    },
  });

  return {
    deleteUser: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
