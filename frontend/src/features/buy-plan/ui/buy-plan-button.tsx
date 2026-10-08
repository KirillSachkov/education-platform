"use client";

import { routes } from "@/shared/config/routes";
import { trackGrowthEvent } from "@/shared/analytics";
import {
  buildPricingCheckoutCallback,
  readPricingIntent,
  storePricingIntent,
} from "@/shared/lib/pricing-intent";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { useSession } from "next-auth/react";
import { useRouter } from "next/navigation";
import { useBuyPlan } from "../model/use-buy-plan";

interface BuyPlanButtonProps {
  planId: string;
  planSlug: string;
  priceCents: number;
  currency: string;
  className?: string;
  size?: "default" | "sm" | "lg";
  /**
   * Переопределяет текст кнопки в authenticated-состоянии (по умолчанию
   * «Оплатить {цена}»). Напр. «Попробовать месяц» для trial-плана (#595).
   * Анонимная ветка всегда «Войти и оплатить {цена}» — auth-контекст важнее.
   */
  label?: string;
}

/**
 * CTA-кнопка покупки плана. На клик POST'ит `/access/orders/` с
 * Idempotency-Key и редиректит юзера на T-Bank paymentUrl.
 *
 * Анонимные юзеры видят CTA «Войти и купить». По клику сохраняется pricing
 * intent, а callbackUrl возвращает на `/pricing` для безопасного продолжения
 * checkout после входа.
 *
 * Caller проверяет условия отображения (priceCents != null && !isOwned) —
 * кнопка сама по себе ничего не валидирует кроме auth.
 */
export function BuyPlanButton({
  planId,
  planSlug,
  priceCents,
  currency,
  className,
  size = "lg",
  label,
}: BuyPlanButtonProps) {
  const session = useSession();
  const router = useRouter();
  const { buy, isPending } = useBuyPlan();
  const priceLabel = formatPrice(priceCents, currency);

  const startAnonymousCheckout = () => {
    const intent = storePricingIntent(planSlug);
    if (!intent) {
      router.push(routes.login);
      return;
    }

    trackGrowthEvent(
      {
        name: "plan_selected",
        properties: {
          plan_id: planSlug,
          placement: "pricing",
          correlation_id: intent.intentId,
        },
      },
      { once: `pricing-plan:${intent.intentId}` },
    );
    trackGrowthEvent(
      {
        name: "auth_started",
        properties: {
          flow: "checkout",
          correlation_id: intent.intentId,
        },
      },
      { once: `pricing-auth-started:${intent.intentId}` },
    );
    const callbackUrl = buildPricingCheckoutCallback(intent);
    router.push(`${routes.login}?${new URLSearchParams({ callbackUrl }).toString()}`);
  };

  // На SSR session.status === "loading" — показываем loading-кнопку, чтобы
  // не было flash от анонимного состояния.
  if (session.status === "loading") {
    return (
      <Button type="button" size={size} className={className} disabled>
        <Icons.loading className="size-4 animate-spin" />
      </Button>
    );
  }

  const startCheckout = () => {
    const storedIntent = readPricingIntent();
    const intent =
      storedIntent?.planSlug === planSlug ? storedIntent : storePricingIntent(planSlug);

    if (intent) {
      trackGrowthEvent(
        {
          name: "plan_selected",
          properties: {
            plan_id: planSlug,
            placement: "pricing",
            correlation_id: intent.intentId,
          },
        },
        { once: `pricing-plan:${intent.intentId}` },
      );
    }

    buy({
      planId,
      planSlug,
      ...(intent ? { correlationId: intent.intentId } : {}),
    });
  };

  if (session.status !== "authenticated") {
    return (
      <Button type="button" size={size} className={className} onClick={startAnonymousCheckout}>
        Войти и оплатить {priceLabel}
      </Button>
    );
  }

  return (
    <Button
      type="button"
      size={size}
      className={className}
      disabled={isPending}
      onClick={startCheckout}
    >
      {isPending ? (
        <>
          <Icons.loading className="size-4 animate-spin" />
          Открываем оплату…
        </>
      ) : (
        <>{label ?? `Оплатить ${priceLabel}`}</>
      )}
    </Button>
  );
}

function formatPrice(priceCents: number, currency: string): string {
  const value = priceCents / 100;
  const symbol = currency === "RUB" ? "₽" : currency;
  return `${value.toLocaleString("ru-RU")} ${symbol}`;
}
