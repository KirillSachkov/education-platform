import { usersAdminApi, usersQueryOptions, type AdminUpdateUserRequest } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateUser() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ userId, request }: { userId: string; request: AdminUpdateUserRequest }) =>
      usersAdminApi.updateUser(userId, request),
    onSuccess: async () => {
      toast.success("Пользователь обновлён");
      await queryClient.invalidateQueries({
        queryKey: [usersQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления пользователя"));
    },
  });

  return {
    updateUser: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
