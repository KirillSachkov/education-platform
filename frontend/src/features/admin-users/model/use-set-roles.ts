import { usersAdminApi, usersQueryOptions, type AdminSetRolesRequest } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useSetRoles() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: ({ userId, request }: { userId: string; request: AdminSetRolesRequest }) =>
      usersAdminApi.setRoles(userId, request),
    onSuccess: async () => {
      toast.success("Роли обновлены");
      await queryClient.invalidateQueries({
        queryKey: [usersQueryOptions.baseKey],
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления ролей"));
    },
  });

  return {
    setRoles: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
