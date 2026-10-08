"use client";

import { adminCrossServiceApi } from "@/entities/admin-cross-service";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateAccessCommunities } from "./invalidate";

/**
 * Admin support-action (#444): повторно (force) отправить приветствие плана юзеру
 * в Telegram DM. Backend возвращает `outcome` — маппим в осмысленный русский toast.
 */
export function useResendPlanWelcome(userId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ planId }: { planId: string }) =>
      adminCrossServiceApi.resendPlanWelcome(userId, planId),
    onSuccess: async (data) => {
      switch (data.result?.outcome) {
        case "Sent":
          toast.success("Приветствие отправлено");
          break;
        case "AlreadySent":
          toast.info("Приветствие уже было отправлено");
          break;
        case "NoWelcomeConfigured":
          toast.info("Для плана не настроено приветствие");
          break;
        case "NotLinked":
          toast.warning("Пользователь не привязал Telegram");
          break;
        case "NoChatBound":
          toast.warning("К плану не привязан Telegram-чат");
          break;
        case "SendFailed":
          toast.error("Не удалось отправить приветствие");
          break;
        default:
          toast.info("Действие выполнено");
      }
      await invalidateAccessCommunities(qc, userId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось отправить приветствие")),
  });
}
