import { planTelegramChatApi, planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import type { UpdateChatBindingFlagsRequest } from "@/entities/plan-telegram-chat";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateChatFlags(planId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      bindingId,
      request,
    }: {
      bindingId: string;
      request: UpdateChatBindingFlagsRequest;
    }) => planTelegramChatApi.updateFlags(bindingId, request),
    onSuccess: async () => {
      toast.success("Настройки обновлены");
      await queryClient.invalidateQueries({
        queryKey: planTelegramChatQueryOptions.list(planId).queryKey,
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка обновления")),
  });
}
