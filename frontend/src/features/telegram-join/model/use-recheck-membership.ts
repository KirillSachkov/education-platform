"use client";

import {
  telegramOnboardingApi,
  telegramOnboardingStatusQueryOptions,
} from "@/entities/plan-onboarding";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * User-triggered recheck Telegram-членства для standalone-страницы `/telegram/join`.
 *
 * Покрывает кейс, когда `chat_member.confirmed` event потерян/опоздал. На
 * `completed:true` — инвалидируем telegram-status (карточка перерисуется в
 * «вы уже в группе») и onboarding (если юзер пришёл из незавершённого онбординга,
 * шаг продвинется). На not-completed мутация резолвится с данными — caller
 * показывает hint, не блокируем.
 *
 * Своя копия (а не импорт из `features/onboarding-wizard`) — FSD запрещает
 * cross-slice импорты между features; API-слой (`entities/plan-onboarding`)
 * лежит ниже и переиспользуется обоими.
 */
export function useRecheckMembership(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => telegramOnboardingApi.recheckMembership(planId),
    onSuccess: async (data) => {
      if (data.result?.completed) {
        toast.success("Вступление подтверждено");
        await Promise.all([
          queryClient.invalidateQueries({
            queryKey: telegramOnboardingStatusQueryOptions(planId).queryKey,
          }),
          queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] }),
        ]);
      }
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось проверить вступление")),
  });
}
