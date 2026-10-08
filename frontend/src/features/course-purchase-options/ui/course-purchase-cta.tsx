"use client";

import Link from "next/link";
import { formatPriceFromCents, sortPublicPlans, type PublicPlanDto } from "@/entities/access-plan";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { useCoursePurchaseOptions } from "../model/use-course-purchase-options";

interface CoursePurchaseCtaProps {
  courseId: string;
  className?: string;
}

/**
 * CTA «Этот курс входит в планы» (#404) — рендерится на лендинге курса для
 * НЕ-купившего юзера. Показывает компактный список планов, которые покрывают
 * курс (FULL_ALL + bundle/single COURSE-планы), каждый ведёт на свою страницу
 * плана. Если покрывающих планов нет — мягкий fallback «Выбрать план» → /pricing.
 */
export function CoursePurchaseCta({ courseId, className }: CoursePurchaseCtaProps) {
  const { coveringPlans, isLoading } = useCoursePurchaseOptions(courseId);

  if (isLoading) return null;

  if (coveringPlans.length === 0) {
    return (
      <Link
        href={routes.pricing}
        className={cn(
          "flex items-center justify-center gap-2 rounded-xl border border-border/60 bg-muted/20 px-4 py-3 text-sm font-medium text-muted-foreground transition-colors hover:border-primary/40 hover:bg-muted/40 hover:text-foreground",
          className,
        )}
      >
        <Icons.layers className="size-4" />
        Выбрать план доступа
      </Link>
    );
  }

  const sorted = sortPublicPlans(coveringPlans);

  return (
    <Card className={cn("gap-0 border-border/60 p-4 sm:p-5", className)}>
      <div className="flex items-center gap-2">
        <span className="flex size-8 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Icons.layers className="size-4" />
        </span>
        <h3 className="text-sm font-semibold tracking-tight">Этот курс входит в планы</h3>
      </div>
      <p className="mt-2 text-xs leading-relaxed text-muted-foreground">
        Полный доступ к .NET Fullstack включает материалы, задания, AI-ревью PR, закрытый
        Telegram-чат, ответы автора и помощь с резюме и поиском работы. Потоков нет — начинаете
        сразу.
      </p>
      <ul className="mt-3 space-y-2">
        {sorted.map((plan) => (
          <li key={plan.id}>
            <PlanRow plan={plan} />
          </li>
        ))}
      </ul>
    </Card>
  );
}

function PlanRow({ plan }: { plan: PublicPlanDto }) {
  const hasPrice = plan.priceCents != null && plan.priceCents > 0;
  const promoActive = hasPrice && plan.promotionActive && plan.effectivePriceCents != null;
  const effectiveCents = promoActive
    ? plan.effectivePriceCents!
    : hasPrice
      ? plan.priceCents!
      : null;

  return (
    <Link
      href={routes.pricingPlanDetail(plan.slug)}
      className="group flex items-center gap-3 rounded-lg border border-border/50 bg-card/60 px-3.5 py-2.5 transition-all duration-200 hover:-translate-y-0.5 hover:border-primary/40 hover:bg-card"
    >
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-medium text-foreground transition-colors group-hover:text-primary">
          {plan.displayName}
        </span>
      </span>
      <span className="flex shrink-0 items-center gap-1.5">
        {promoActive && plan.discountPercent ? (
          <span className="rounded-full bg-rose-500/15 px-1.5 py-0.5 text-[10px] font-semibold text-rose-600 dark:text-rose-400">
            −{plan.discountPercent}%
          </span>
        ) : null}
        <span className="text-sm font-semibold tabular-nums">
          {effectiveCents != null
            ? formatPriceFromCents(effectiveCents, plan.currency)
            : "По запросу"}
        </span>
      </span>
      <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/40 transition-all duration-200 group-hover:translate-x-0.5 group-hover:text-primary" />
    </Link>
  );
}
