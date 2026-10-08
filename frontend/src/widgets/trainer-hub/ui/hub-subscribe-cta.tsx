"use client";

import { useQuery } from "@tanstack/react-query";
import { formatPriceFromCents, hasTrainerProGrant, myGrantsQueryOptions } from "@/entities/access-plan";
import { resolveTrainerProOfferCard, trainerProOfferQueryOptions } from "@/entities/trainer-pro";
import { BuyTrainerProButton } from "@/features/buy-trainer-pro";
import { useIsAuthenticated, useRoles } from "@/shared/auth";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";

/**
 * SubscribeCta (#614, workstream G) — баннер «Оформить Тренажёр Pro» над вкладками
 * хаба. Показывается залогиненному юзеру БЕЗ активной PRO-подписки (PRO-холдеру и
 * админу не показываем — у них доступ уже открыт). Резолвит оффер через выделенный
 * trainer-API (`GET /access/trainer-pro/offer/`) и `BuyTrainerProButton` (он сам ведёт
 * анонима на логин и инициирует подписку server-side через `/access/trainer-pro/orders/`).
 *
 * Гейт: authenticated && !hasPro && !admin. Анонимам не показываем — их и так
 * подталкивают на логин per-content локи; дублировать не нужно.
 */
export function HubSubscribeCta() {
  const isAuthenticated = useIsAuthenticated();
  const { isAtLeast } = useRoles();
  const isAdmin = isAtLeast("platform-admin");

  const { data: offers } = useQuery({
    ...trainerProOfferQueryOptions(),
    enabled: isAuthenticated && !isAdmin,
  });
  const { data: grants } = useQuery({
    ...myGrantsQueryOptions(),
    enabled: isAuthenticated && !isAdmin,
  });

  // Только залогиненному без PRO и не админу. Аноним / админ / PRO-холдер → ничего.
  if (!isAuthenticated || isAdmin) return null;
  if (grants && hasTrainerProGrant(grants)) return null;

  // Нет опубликованного Trainer Pro оффера или у него нет цены — баннер не рендерим
  // (нечего покупать). Цена — эффективная (с учётом акции), как на pricing.
  const offer = resolveTrainerProOfferCard(offers);
  if (!offer) return null;

  const cadence = offer.monthly ? "/мес" : null;
  const priceLabel = `${formatPriceFromCents(offer.priceCents, offer.currency)}${cadence ?? ""}`;

  return (
    <section
      className={cn(
        "relative overflow-hidden rounded-2xl border border-violet-500/30 p-5 shadow-sm sm:p-6",
        "bg-gradient-to-br from-violet-500/[0.08] via-card to-card",
      )}
    >
      <div
        aria-hidden="true"
        className="pointer-events-none absolute -right-16 -top-16 size-48 rounded-full bg-violet-500/15 blur-3xl"
      />
      <div className="relative flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          <span className="mt-0.5 inline-flex size-9 shrink-0 items-center justify-center rounded-xl bg-violet-500/15 text-violet-500 dark:text-violet-300">
            <Icons.energy className="size-5" />
          </span>
          <div className="min-w-0">
            <p className="text-[11px] font-semibold uppercase tracking-wider text-violet-500 dark:text-violet-300">
              Тренажёр Pro
            </p>
            <h2 className="mt-1 text-base font-bold tracking-tight sm:text-lg">
              Открой тренажёр без лимитов
            </h2>
            <p className="mt-1 text-pretty text-sm leading-relaxed text-muted-foreground">
              Голосовые ответы, мок-интервью, повторы и все темы — по подписке.
            </p>
          </div>
        </div>
        <div className="shrink-0 sm:max-w-xs">
          <BuyTrainerProButton
            planId={offer.planId}
            priceCents={offer.priceCents}
            currency={offer.currency}
            size="default"
            className="w-full bg-violet-600 text-white hover:bg-violet-600/90 sm:w-auto"
            label={`Оформить Тренажёр Pro — ${priceLabel}`}
          />
        </div>
      </div>
    </section>
  );
}
