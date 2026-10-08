"use client";

import {
  planOnboardingApi,
  telegramOnboardingApi,
  type CurrentOnboardingResponse,
} from "@/entities/plan-onboarding";
import { getErrorMessage, isEnvelopeError, type Envelope } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { routes } from "@/shared/config/routes";

export function useSkipStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (stepId: string) => planOnboardingApi.skipStep(planId, stepId),
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось пропустить шаг")),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] }),
  });
}

export function useCompleteStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (stepId: string) => planOnboardingApi.completeStep(planId, stepId),
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось завершить шаг")),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] }),
  });
}

export function useReturnToStep(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (stepId: string) => planOnboardingApi.returnToStep(planId, stepId),
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось вернуться к шагу")),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] }),
  });
}

/**
 * User-triggered recheck Telegram-членства (epic #397). Покрывает кейс, когда
 * `chat_member.confirmed` event был потерян/опоздал. На `completed:true` —
 * инвалидируем onboarding query, wizard сам продвинется на следующий шаг.
 * На not-completed мутация резолвится с данными — caller показывает hint
 * (шаг остаётся skippable, не блокируем).
 */
export function useRecheckTelegramMembership(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => telegramOnboardingApi.recheckMembership(planId),
    onSuccess: async (data) => {
      if (data.result?.completed) {
        toast.success("Вступление подтверждено");
        await queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] });
      }
    },
    onError: (error) =>
      toast.error(getErrorMessage(error, "Не удалось проверить вступление")),
  });
}

export function useCompleteOnboarding(planId: string) {
  const queryClient = useQueryClient();
  const router = useRouter();
  return useMutation({
    mutationFn: () => planOnboardingApi.completeOnboarding(planId),
    onSuccess: () => {
      toast.success("Онбординг пройден");
      // Гасим overlay синхронно: setQueryData НОТИФИЦИРУЕТ активный observer
      // (`OnboardingOverlay`) → `data` становится null → Dialog закрывается сразу.
      // removeQueries чистил кэш, но observer НЕ нотифицировал — а router.push на
      // тот же /home (пост-покупочный сценарий) re-render не вызывает, и модалка
      // залипала (закрывалась только от постороннего ре-рендера, напр. клик мимо).
      // Бэк после complete отдаёт current=null, поэтому фоновый рефетч консистентен.
      queryClient.setQueryData<Envelope<CurrentOnboardingResponse | null>>(
        ["plan-onboarding", "current"],
        (old) => (old ? { ...old, result: null } : old),
      );
      // …и уводим в основное обучение.
      router.push(routes.home);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось завершить онбординг"));
      // #497: 409 has.pending.steps = flow изменился после того, как юзер дошёл
      // до «старого конца» (stale-кэш показал CompletionView). Ресинк current —
      // сервер self-heal'ит курсор, wizard перепрыгнет на pending шаг вместо тупика.
      if (
        isEnvelopeError(error) &&
        error.apiError.messages.some((m) => m.code === "onboarding.has.pending.steps")
      ) {
        void queryClient.invalidateQueries({ queryKey: ["plan-onboarding", "current"] });
      }
    },
  });
}
