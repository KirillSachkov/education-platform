"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { myGrantsQueryOptions, type PlanTier } from "@/entities/access-plan";
import { useIsAuthenticated, useRoles } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";

const TIER_LABEL: Record<PlanTier, string> = {
  FULL_ALL: "Полный доступ",
  LEARN_ALL: "Все материалы",
  COURSE: "Доступ к курсу",
  SUBSCRIPTION: "Подписка",
  FREE: "Бесплатный доступ",
};

// Приоритет от «самого полного» к «частичному» — показываем лучший активный план.
const TIER_PRIORITY: PlanTier[] = ["FULL_ALL", "LEARN_ALL", "SUBSCRIPTION", "COURSE", "FREE"];

/**
 * Индикатор текущего плана пользователя в подвале сайдбара.
 * - Админ/автор → «Полный доступ» (доступ по роли, без grant'а).
 * - Иначе — лучший активный plan-grant по приоритету tier'ов (tier живёт в grant.plan).
 * - Нет плана → CTA «Выбрать план» на /pricing.
 * В свёрнутом сайдбаре (`collapsible=icon`) текст скрыт, остаётся иконка короны.
 */
export function SidebarPlanBadge() {
  const isAuthenticated = useIsAuthenticated();
  const { isAtLeast } = useRoles();
  const isAdmin = isAtLeast("platform-author");

  const { data: grants } = useQuery({
    ...myGrantsQueryOptions(),
    enabled: isAuthenticated && !isAdmin,
  });

  if (!isAuthenticated) return null;

  const activeTiers = (grants ?? [])
    .filter((g) => g.status === "ACTIVE")
    .map((g) => g.plan?.tier)
    .filter((t): t is PlanTier => Boolean(t));

  const bestTier: PlanTier | null = isAdmin
    ? "FULL_ALL"
    : (TIER_PRIORITY.find((t) => activeTiers.includes(t)) ?? null);

  // Нет активного плана → приглашение выбрать.
  if (!bestTier) {
    return (
      <Link
        href={routes.pricing}
        title="Выбрать план"
        className="flex items-center gap-2 rounded-lg border border-dashed border-sidebar-border px-2.5 py-2 text-sm text-muted-foreground transition-colors hover:border-primary/40 hover:text-foreground"
      >
        <Icons.crown className="size-4 shrink-0 text-primary/70" />
        <span className="truncate group-data-[collapsible=icon]:hidden">Выбрать план</span>
      </Link>
    );
  }

  return (
    <Link
      href={routes.pricing}
      title={`Ваш план: ${TIER_LABEL[bestTier]}`}
      className="flex items-center gap-2 rounded-lg border border-sidebar-border bg-sidebar-accent/40 px-2.5 py-2 transition-colors hover:bg-sidebar-accent"
    >
      <Icons.crown className="size-4 shrink-0 text-amber-500 dark:text-amber-300" />
      <div className="grid min-w-0 leading-tight group-data-[collapsible=icon]:hidden">
        <span className="truncate text-[11px] text-muted-foreground">Ваш план</span>
        <span className="truncate text-sm font-medium text-foreground">{TIER_LABEL[bestTier]}</span>
      </div>
    </Link>
  );
}
