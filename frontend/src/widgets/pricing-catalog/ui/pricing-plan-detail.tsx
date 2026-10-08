"use client";

import { publicPlanBySlugQueryOptions, upgradeQuoteQueryOptions } from "@/entities/access-plan";
import { BuyPlanButton } from "@/features/buy-plan";
import { useBillingEnabled } from "@/entities/billing-config";
import { useIsAuthenticated } from "@/shared/auth/use-is-authenticated";
import { personalPriceParts } from "../lib/personal-price";
import { UpgradeCreditBreakdown } from "./upgrade-credit-breakdown";
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";

interface PricingPlanDetailProps {
  slug: string;
}

export function PricingPlanDetail({ slug }: PricingPlanDetailProps) {
  const planQuery = useQuery(publicPlanBySlugQueryOptions(slug));
  const billingEnabled = useBillingEnabled();
  const isAuthenticated = useIsAuthenticated();

  // #486: персональная цена с вычетом за уже купленные планы — тот же quote,
  // что и в каталоге; сумма на кнопке обязана совпасть со списанием CreateOrder.
  const { data: quote } = useQuery({
    ...upgradeQuoteQueryOptions(planQuery.data?.id),
    enabled: isAuthenticated && Boolean(planQuery.data?.id),
  });

  if (planQuery.isLoading) {
    return (
      <div className="mx-auto mt-12 max-w-3xl px-4">
        <div className="h-12 w-2/3 rounded bg-muted animate-pulse" />
        <div className="mt-6 h-64 rounded bg-muted animate-pulse" />
      </div>
    );
  }

  if (planQuery.isError || !planQuery.data) {
    return (
      <div className="mx-auto mt-24 max-w-md px-4 text-center">
        <h2 className="text-xl font-semibold">План не найден</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          Возможно, ссылка устарела или автор снял план с публикации.
        </p>
        <Button asChild variant="outline" className="mt-6">
          <Link href={routes.pricing}>К каталогу планов</Link>
        </Button>
      </div>
    );
  }

  const plan = planQuery.data;
  // Промо: показываем эффективную (скидочную) цену. Раньше показывали list-цену priceCents,
  // хотя бэкенд списывает effectivePriceCents — юзер видел одно, платил другое.
  const promoActive =
    plan.priceCents != null && plan.promotionActive && plan.effectivePriceCents != null;
  const effectiveCents = promoActive ? plan.effectivePriceCents! : plan.priceCents;
  // #521: при upgrade-credit главная цена = личная итоговая, эффективная — зачёркнутая
  // рядом. Promo-зачёркивание list-цены в этом случае скрываем — его процент с
  // финальной суммой не сходится.
  const personal =
    effectiveCents != null
      ? personalPriceParts(
          { priceLabel: formatPrice(effectiveCents, plan.currency), currency: plan.currency },
          quote,
        )
      : null;

  return (
    <div className="mx-auto mt-12 max-w-3xl space-y-6 px-4 pb-16">
      <Link
        href={routes.pricing}
        className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:underline"
      >
        <Icons.back className="size-4" />К каталогу
      </Link>

      <header className="space-y-3">
        <h1 className="text-2xl font-semibold sm:text-3xl">{plan.displayName}</h1>
        {plan.shortDescription && (
          <p className="text-lg text-muted-foreground">{plan.shortDescription}</p>
        )}
      </header>

      {plan.priceCents != null && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-baseline gap-3">
            <span className="text-4xl font-semibold">
              {personal?.priceLabel ?? formatPrice(effectiveCents!, plan.currency)}
            </span>
            {personal?.strikeLabel ? (
              <span className="text-xl text-muted-foreground line-through">
                {personal.strikeLabel}
              </span>
            ) : promoActive ? (
              <>
                <span className="text-xl text-muted-foreground line-through">
                  {formatPrice(plan.priceCents, plan.currency)}
                </span>
                {plan.discountPercent ? (
                  <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-sm font-semibold text-rose-600 dark:text-rose-400">
                    −{plan.discountPercent}%
                  </span>
                ) : null}
              </>
            ) : null}
          </div>
          {quote && !quote.isOwned && quote.creditCents > 0 && (
            <UpgradeCreditBreakdown
              quote={quote}
              originalPrice={formatPrice(effectiveCents!, plan.currency)}
            />
          )}
          {quote?.isOwned ? (
            <div className="inline-flex w-full items-center justify-center gap-2 rounded-xl border-2 border-emerald-500/40 bg-emerald-500/5 px-6 py-3.5 text-sm font-semibold text-emerald-600 dark:text-emerald-400 sm:w-auto">
              <Icons.check className="size-4" />У вас уже есть этот план
            </div>
          ) : billingEnabled ? (
            <>
              <BuyPlanButton
                planId={plan.id}
                planSlug={plan.slug}
                priceCents={
                  quote && quote.creditCents > 0 && quote.finalPriceCents != null
                    ? quote.finalPriceCents
                    : effectiveCents!
                }
                currency={plan.currency}
                className="w-full sm:w-auto"
              />
              <p className="text-xs text-muted-foreground">
                Оплата через T-Bank. Чек на email отправит онлайн-касса автоматически.
              </p>
            </>
          ) : (
            // Pre-launch fallback: T-Bank checkout пока выключен флагом,
            // юзер пишет автору в Telegram для ручной выдачи доступа.
            <>
              <Button asChild size="lg" className="w-full sm:w-auto">
                <Link
                  href={PRIMARY_AUTHOR_CONSULTATION_LINK}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Оформить через Telegram
                </Link>
              </Button>
              <p className="text-xs text-muted-foreground">
                Прямая оплата подключается. Пока — напишите автору и он откроет доступ вручную.
              </p>
            </>
          )}
        </div>
      )}

      {plan.longDescription && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Что внутри</CardTitle>
          </CardHeader>
          <CardContent className="prose prose-sm dark:prose-invert max-w-none">
            <p className="whitespace-pre-line">{plan.longDescription}</p>
          </CardContent>
        </Card>
      )}

      {plan.features.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Особенности</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="space-y-2">
              {plan.features.map((feature) => (
                <li key={feature} className="flex items-start gap-2 text-sm">
                  <Icons.completed className="mt-0.5 size-4 shrink-0 text-emerald-500" />
                  <span>{feature}</span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function formatPrice(priceCents: number, currency: string): string {
  const value = priceCents / 100;
  const symbol = currency === "RUB" ? "₽" : currency;
  return `${value.toLocaleString("ru-RU")} ${symbol}`;
}
