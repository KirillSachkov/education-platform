"use client";

import Link from "next/link";
import { formatPriceFromCents, sortPublicPlans, type PublicPlanDto } from "@/entities/access-plan";
import { getOfferTypeBadge } from "@/shared/config/offer-type";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";

interface CoursePlansDropdownProps {
  /** Планы, покрывающие курс (FULL_ALL + bundle/single COURSE). Уже отфильтрованы caller'ом. */
  coveringPlans: PublicPlanDto[];
  className?: string;
}

/**
 * Компактный chip-триггер «входит в N планов» с Popover'ом, в котором перечислены
 * планы, покрывающие курс — каждый ведёт на свою страницу плана (`/pricing/<slug>`).
 *
 * Каталожный аналог `CoursePurchaseCta` (лендинг курса): та же логика эффективной
 * цены + промо-бейджа, но в свёрнутом виде, чтобы не раздувать карточку.
 *
 * Рендерится только когда есть хоть один покрывающий план (иначе `null`).
 * A11y / позиционирование / keyboard-навигация — из Radix Popover (`shared/ui/kit`):
 * триггер — нативная кнопка, контент в top-layer портале с focus-trap и Esc-close,
 * автоматический flip у края вьюпорта.
 */
export function CoursePlansDropdown({ coveringPlans, className }: CoursePlansDropdownProps) {
  if (coveringPlans.length === 0) return null;

  const sorted = sortPublicPlans(coveringPlans);
  const count = sorted.length;
  const triggerLabel = `входит в ${count} ${pluralize(count, "план", "плана", "планов")}`;

  return (
    <Popover>
      <PopoverTrigger
        className={cn(
          "group inline-flex max-w-full items-center gap-1 rounded text-[11px] font-medium text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
          className,
        )}
      >
        <Icons.layers className="size-3 shrink-0 opacity-60" />
        <span className="truncate">{triggerLabel}</span>
        <Icons.chevronRight className="size-3 shrink-0 rotate-90 text-muted-foreground/50 transition-transform group-data-[state=open]:-rotate-90" />
      </PopoverTrigger>
      <PopoverContent align="start" className="w-72 p-2">
        <p className="px-1.5 pb-1.5 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
          Входит в планы
        </p>
        <ul className="space-y-1">
          {sorted.map((plan) => (
            <li key={plan.id}>
              <PlanRow plan={plan} />
            </li>
          ))}
        </ul>
      </PopoverContent>
    </Popover>
  );
}

/**
 * Строка плана в дропдауне: название + эффективная цена + промо-бейдж «−N%».
 * Зеркалит `PlanRow` из `course-purchase-cta` (лендинг курса) — promo берётся из
 * `promotionActive` + `effectivePriceCents`, считает бэкенд в read-time.
 */
function PlanRow({ plan }: { plan: PublicPlanDto }) {
  const hasPrice = plan.priceCents != null && plan.priceCents > 0;
  const promoActive = hasPrice && plan.promotionActive && plan.effectivePriceCents != null;
  const effectiveCents = promoActive
    ? plan.effectivePriceCents!
    : hasPrice
      ? plan.priceCents!
      : null;

  const offerBadge = getOfferTypeBadge(plan.offerType);
  // Формат-тег показываем только для интенсивов/марафонов — он добавляет смысл.
  // У «Полного доступа» бейдж дублировал бы название, у обычного курса его нет.
  const showFormatTag = plan.offerType === "INTENSIVE" || plan.offerType === "MARATHON";

  return (
    <Link
      href={routes.pricingPlanDetail(plan.slug)}
      className="group flex items-start gap-2 rounded-lg border border-border/50 bg-card/60 px-3 py-2 transition-all duration-200 hover:-translate-y-0.5 hover:border-primary/40 hover:bg-card"
    >
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-medium leading-snug text-foreground transition-colors line-clamp-2 group-hover:text-primary">
          {plan.displayName}
        </span>
        <span className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="text-sm font-semibold tabular-nums text-foreground">
            {effectiveCents != null
              ? formatPriceFromCents(effectiveCents, plan.currency)
              : "По запросу"}
          </span>
          {promoActive && plan.discountPercent ? (
            <span className="rounded-full bg-rose-500/15 px-1.5 py-0.5 text-[10px] font-semibold text-rose-600 dark:text-rose-400">
              −{plan.discountPercent}%
            </span>
          ) : null}
          {showFormatTag && offerBadge ? (
            <span
              className={cn(
                "inline-flex items-center rounded-md px-1.5 py-0.5 text-[10px] font-semibold",
                offerBadge.class,
              )}
            >
              {offerBadge.label}
            </span>
          ) : null}
        </span>
      </span>
      <Icons.chevronRight className="mt-0.5 size-4 shrink-0 text-muted-foreground/40 transition-all duration-200 group-hover:translate-x-0.5 group-hover:text-primary" />
    </Link>
  );
}
