"use client";

import { adminCrossServiceApi } from "@/entities/admin-cross-service";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateAccessCommunities } from "./invalidate";

/**
 * Admin support-action (#444): перепроверить Telegram-членство юзера в чате плана
 * и докрутить onboarding-шаг. Backend soft-degrade'ит (TBS недоступен → completed=false),
 * поэтому маппим результат в осмысленный русский toast вместо «успех/ошибка».
 */
export function useRecheckTelegramMembership(userId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ planId }: { planId: string }) =>
      adminCrossServiceApi.recheckTelegramMembership(userId, planId),
    onSuccess: async (data) => {
      const result = data.result;
      if (result?.completed) {
        toast.success("Telegram-членство подтверждено, шаг закрыт");
      } else if (result?.status === "member") {
        toast.success("Пользователь уже в чате");
      } else if (result?.status === "not_member") {
        toast.warning("Пользователь пока не в чате");
      } else {
        toast.info("Проверка выполнена, статус не изменился");
      }
      await invalidateAccessCommunities(qc, userId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось перепроверить членство")),
  });
}
