import { billingConfigApi, billingConfigQueryKey } from "@/entities/billing-config";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/** Админ включает/выключает приём прямой оплаты (T-Bank). */
export function useUpdateBillingConfig() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (isEnabled: boolean) => billingConfigApi.updateConfig(isEnabled),
    onSuccess: async (data) => {
      toast.success(data.result?.isEnabled ? "Приём оплаты включён" : "Приём оплаты выключен");
      await queryClient.invalidateQueries({ queryKey: billingConfigQueryKey });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка обновления оплаты")),
  });
}
