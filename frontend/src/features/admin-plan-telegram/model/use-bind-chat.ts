import { planTelegramChatApi, planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import type { BindChatRequest } from "@/entities/plan-telegram-chat";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useBindChat(planId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: BindChatRequest) => planTelegramChatApi.bind(planId, request),
    onSuccess: async () => {
      toast.success("Чат привязан");
      await queryClient.invalidateQueries({
        queryKey: planTelegramChatQueryOptions.list(planId).queryKey,
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка привязки чата")),
  });
}
