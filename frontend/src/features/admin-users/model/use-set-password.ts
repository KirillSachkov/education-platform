import { usersAdminApi, usersQueryOptions, type AdminSetPasswordRequest } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useSetPassword() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ userId, request }: { userId: string; request: AdminSetPasswordRequest }) =>
      usersAdminApi.setPassword(userId, request),
    onSuccess: async () => {
      toast.success("Пароль изменён");
      await queryClient.invalidateQueries({
        queryKey: [usersQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка смены пароля"));
    },
  });

  return {
    setPassword: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
