import { usersAdminApi, usersQueryOptions, type AdminSetLockoutRequest } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useSetLockout() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ userId, request }: { userId: string; request: AdminSetLockoutRequest }) =>
      usersAdminApi.setLockout(userId, request),
    onSuccess: async () => {
      toast.success("Статус блокировки обновлён");
      await queryClient.invalidateQueries({
        queryKey: [usersQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка изменения блокировки"));
    },
  });

  return {
    setLockout: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
