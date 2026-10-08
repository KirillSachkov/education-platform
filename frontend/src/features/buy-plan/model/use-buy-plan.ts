"use client";

import { accessOrderApi } from "@/entities/access-order";
import { trackGrowthEvent } from "@/shared/analytics";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

const LAST_ORDER_STORAGE_KEY = "billing.lastOrderId";

/**
 * Per-plan хранилище Idempotency-Key'ев в `window.__billingIdempotencyKeys` —
 * Map planId → UUID. Защита от двойного клика на тот же план (тот же ключ →
 * backend вернёт закешированный response). Перенавигация на ДРУГОЙ план
 * получает новый ключ → не попадает в кеш-миш плана-предшественника
 * (иначе backend бы вернул paymentUrl чужого плана).
 *
 * Reset на full reload — на новой сессии все ключи свежие.
 */
function getOrCreateIdempotencyKeyForPlan(planId: string): string {
  if (typeof window === "undefined") return "";
  const w = window as unknown as { __billingIdempotencyKeys?: Map<string, string> };
  if (!w.__billingIdempotencyKeys) {
    w.__billingIdempotencyKeys = new Map<string, string>();
  }
  let key = w.__billingIdempotencyKeys.get(planId);
  if (!key) {
    key = crypto.randomUUID();
    w.__billingIdempotencyKeys.set(planId, key);
  }
  return key;
}

export interface UseBuyPlanResult {
  buy: (request: BuyPlanInput) => void;
  isPending: boolean;
}

export interface BuyPlanInput {
  planId: string;
  planSlug: string;
  correlationId?: string;
}

/**
 * Создаёт Order через бэкенд + редиректит юзера на T-Bank `paymentUrl`.
 * orderId сохраняется в `sessionStorage` (per-tab, чтобы вторая вкладка с
 * другой покупкой не перезаписала наш orderId) на /payment/success как fallback
 * если T-Bank не приклеит OrderId к query.
 */
export function useBuyPlan(): UseBuyPlanResult {
  const mutation = useMutation({
    mutationFn: ({ planId, planSlug, correlationId }: BuyPlanInput) => {
      const idempotencyKey = correlationId ?? getOrCreateIdempotencyKeyForPlan(planId);
      trackGrowthEvent(
        {
          name: "checkout_started",
          properties: {
            plan_id: planSlug,
            ...(correlationId ? { correlation_id: correlationId } : {}),
          },
        },
        correlationId ? { once: `pricing-checkout:${correlationId}` } : {},
      );
      return accessOrderApi.createOrder({ planId }, idempotencyKey);
    },
    onSuccess: (envelope) => {
      const result = envelope.result;
      if (!result) {
        toast.error("Не удалось получить ссылку на оплату");
        return;
      }
      try {
        // sessionStorage — per-tab. Если юзер открыл вторую вкладку с другой
        // покупкой, она не перетрёт наш orderId.
        sessionStorage.setItem(LAST_ORDER_STORAGE_KEY, result.orderId);
      } catch {
        // Privacy mode / quota: не критично, /payment/success прочтёт orderId
        // из query, который T-Bank сам приклеит.
      }
      window.location.href = result.paymentUrl;
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось начать оплату"));
    },
  });

  return {
    buy: (request: BuyPlanInput) => {
      mutation.mutate(request);
    },
    isPending: mutation.isPending,
  };
}

/** Читает orderId из sessionStorage. Используется на /payment/success как fallback. */
export function readLastOrderId(): string | null {
  try {
    return sessionStorage.getItem(LAST_ORDER_STORAGE_KEY);
  } catch {
    return null;
  }
}

/** Очищает orderId из sessionStorage после терминального статуса. */
export function clearLastOrderId(): void {
  try {
    sessionStorage.removeItem(LAST_ORDER_STORAGE_KEY);
  } catch {
    // ignore
  }
}
