"use client";

import type { PublicPlanDto } from "@/entities/access-plan";
import { getOfferTypeBadge } from "@/shared/config/offer-type";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import Link from "next/link";

interface PlanCardProps {
  plan: PublicPlanDto;
  /** True if the current user already has an active grant on this plan. */
  isOwned?: boolean;
}

export function PlanCard({ plan, isOwned }: PlanCardProps) {
  const offerBadge = getOfferTypeBadge(plan.offerType);
  // Промо: эффективная цена + зачёркнутая исходная (как catalog-plans-view).
  const promoActive =
    plan.priceCents != null && plan.promotionActive && plan.effectivePriceCents != null;
  const effectiveCents = promoActive ? plan.effectivePriceCents! : plan.priceCents;
  return (
    <Card className="flex h-full flex-col">
      <CardHeader>
        <div className="flex items-center gap-2">
          <CardTitle className="text-xl">{plan.displayName}</CardTitle>
          {offerBadge ? (
            <span
              className={cn(
                "inline-flex shrink-0 items-center rounded-md px-2 py-0.5 text-[11px] font-semibold",
                offerBadge.class,
              )}
            >
              {offerBadge.label}
            </span>
          ) : null}
        </div>
        {plan.shortDescription && <CardDescription>{plan.shortDescription}</CardDescription>}
      </CardHeader>
      <CardContent className="flex-1 space-y-4">
        {plan.priceCents != null && (
          <div className="flex flex-wrap items-baseline gap-2">
            <span className="text-3xl font-semibold">
              {formatPrice(effectiveCents!, plan.currency)}
            </span>
            {promoActive && (
              <span className="text-base text-muted-foreground line-through">
                {formatPrice(plan.priceCents, plan.currency)}
              </span>
            )}
          </div>
        )}
        {plan.features.length > 0 && (
          <ul className="space-y-2">
            {plan.features.map((feature) => (
              <li key={feature} className="flex items-start gap-2 text-sm">
                <Icons.completed className="mt-0.5 size-4 shrink-0 text-emerald-500" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        )}
        {plan.tier === "FULL_ALL" && (
          <p className="text-xs text-muted-foreground">
            Включая новые материалы и курсы программы, которые выйдут позже.
          </p>
        )}
      </CardContent>
      <CardFooter>
        {isOwned ? (
          <Button variant="secondary" disabled className="w-full">
            У вас уже этот план
          </Button>
        ) : (
          <Button asChild className="w-full">
            <Link href={routes.pricingPlanDetail(plan.slug)}>Подробнее</Link>
          </Button>
        )}
      </CardFooter>
    </Card>
  );
}

function formatPrice(priceCents: number, currency: string): string {
  const value = priceCents / 100;
  const symbol = currency === "RUB" ? "₽" : currency;
  return `${value.toLocaleString("ru-RU")} ${symbol}`;
}
