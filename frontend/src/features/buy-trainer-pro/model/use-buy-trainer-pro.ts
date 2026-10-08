"use client";

import { trainerProApi } from "@/entities/trainer-pro";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

const LAST_ORDER_STORAGE_KEY = "billing.lastOrderId";

/**
 * Per-plan хранилище Idempotency-Key'ев в `window.__trainerProIdempotencyKeys` (Map planId → UUID).
 * Защита от двойного клика на тот же оффер (тот же ключ → backend вернёт закешированный response).
 * Reset на full reload — на новой сессии ключи свежие. Зеркалит паттерн `useBuyPlan`.
 */
function getOrCreateIdempotencyKey(planId: string): string {
  if (typeof window === "undefined") return "";
  const w = window as unknown as { __trainerProIdempotencyKeys?: Map<string, string> };
  if (!w.__trainerProIdempotencyKeys) {
    w.__trainerProIdempotencyKeys = new Map<string, string>();
  }
  let key = w.__trainerProIdempotencyKeys.get(planId);
  if (!key) {
    key = crypto.randomUUID();
    w.__trainerProIdempotencyKeys.set(planId, key);
  }
  return key;
}

export interface UseBuyTrainerProResult {
  buy: (planId: string) => void;
  isPending: boolean;
}

/**
 * Создаёт Order на подписку тренажёра через `POST /access/trainer-pro/orders/` + редиректит юзера
 * на T-Bank `paymentUrl`. orderId сохраняется в `sessionStorage` (per-tab) как fallback для
 * `/payment/success`, если T-Bank не приклеит OrderId к query. Тот же контракт, что у `useBuyPlan`,
 * но против trainer-scoped order-эндпоинта (платформенный `/access/orders/` отвергает TRAINER-планы).
 */
export function useBuyTrainerPro(): UseBuyTrainerProResult {
  const mutation = useMutation({
    mutationFn: (planId: string) =>
      trainerProApi.createOrder({ planId }, getOrCreateIdempotencyKey(planId)),
    onSuccess: (envelope) => {
      const result = envelope.result;
      if (!result) {
        toast.error("Не удалось получить ссылку на оплату");
        return;
      }
      try {
        sessionStorage.setItem(LAST_ORDER_STORAGE_KEY, result.orderId);
      } catch {
        // Privacy mode / quota: не критично, /payment/success прочтёт orderId из query.
      }
      window.location.href = result.paymentUrl;
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось начать оплату"));
    },
  });

  return {
    buy: (planId: string) => mutation.mutate(planId),
    isPending: mutation.isPending,
  };
}
