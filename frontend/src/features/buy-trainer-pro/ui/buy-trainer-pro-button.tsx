"use client";

import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { useSession } from "next-auth/react";
import Link from "next/link";
import { useBuyTrainerPro } from "../model/use-buy-trainer-pro";

interface BuyTrainerProButtonProps {
  planId: string;
  priceCents: number;
  currency: string;
  className?: string;
  size?: "default" | "sm" | "lg";
  /** Переопределяет текст authenticated-кнопки (по умолчанию «Оплатить {цена}»). */
  label?: string;
}

/**
 * CTA-кнопка покупки подписки тренажёра (#674). На клик POST'ит `/access/trainer-pro/orders/` с
 * Idempotency-Key и редиректит на T-Bank paymentUrl. Анонимы видят «Войти и оплатить {цена}» с
 * callbackUrl на текущую страницу. Зеркалит `BuyPlanButton`, но против trainer-scoped order-эндпоинта.
 */
export function BuyTrainerProButton({
  planId,
  priceCents,
  currency,
  className,
  size = "lg",
  label,
}: BuyTrainerProButtonProps) {
  const session = useSession();
  const { buy, isPending } = useBuyTrainerPro();
  const priceLabel = formatPrice(priceCents, currency);

  // На SSR session.status === "loading" — loading-кнопка без flash анонимного состояния.
  if (session.status === "loading") {
    return (
      <Button type="button" size={size} className={className} disabled>
        <Icons.loading className="size-4 animate-spin" />
      </Button>
    );
  }

  if (session.status !== "authenticated") {
    const callbackUrl =
      typeof window !== "undefined" ? window.location.pathname + window.location.search : "";
    return (
      <Button asChild size={size} className={className}>
        <Link
          href={`${routes.login}?callbackUrl=${encodeURIComponent(callbackUrl)}`}
          prefetch={false}
        >
          Войти и оплатить {priceLabel}
        </Link>
      </Button>
    );
  }

  return (
    <Button
      type="button"
      size={size}
      className={className}
      disabled={isPending}
      onClick={() => buy(planId)}
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
