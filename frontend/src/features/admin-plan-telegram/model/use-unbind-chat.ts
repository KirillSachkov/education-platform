import { planTelegramChatApi, planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUnbindChat(planId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (bindingId: string) => planTelegramChatApi.unbind(bindingId),
    onSuccess: async () => {
      toast.success("Чат отвязан");
      await queryClient.invalidateQueries({
        queryKey: planTelegramChatQueryOptions.list(planId).queryKey,
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка отвязки чата")),
  });
}
