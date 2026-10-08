import { usersAdminApi, usersQueryOptions, type AdminBulkLockoutRequest } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useBulkLockout() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (req: AdminBulkLockoutRequest) => usersAdminApi.bulkLockout(req),
    onSuccess: async (data) => {
      const result = data.result;
      const succeeded = result?.succeeded.length ?? 0;
      const failed = result?.failed.length ?? 0;
      if (failed === 0) {
        toast.success(`Применено к ${succeeded} юзерам`);
      } else {
        toast.success(`Применено: ${succeeded}, ошибок: ${failed}`);
      }
      await queryClient.invalidateQueries({ queryKey: [usersQueryOptions.baseKey] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка массового lockout"));
    },
  });

  return mutation;
}
