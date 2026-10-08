"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";

import { myGrantsQueryOptions, type PlanGrantDto } from "@/entities/access-plan";
import { trainerLimitsQueryOptions, type TrainerLimit } from "@/entities/trainer-limits";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";

/**
 * Карточка «Trainer Pro» в правом блоке хаба (#568): статус доступа (Pro / Free, для подписки —
 * сколько дней осталось, для полного доступа — «за полный доступ»), плюс остаток AI-лимитов
 * (проверки открытых ответов в день, голос и мок-собесы в месяц). `isPro` берём из
 * `/trainer/me/limits` (авторитетно, ловит и авто-PRO за полный доступ к платформе), дни — из
 * TRAINER_PRO-подписки. Free-юзеру голос/мок показываем как PRO-фичу + ссылку «Оформить».
 */
export function TrainerProStatusCard() {
  const isAuthenticated = useIsAuthenticated();
  const limitsQuery = useQuery({ ...trainerLimitsQueryOptions(), enabled: isAuthenticated });
  const grantsQuery = useQuery({ ...myGrantsQueryOptions(), enabled: isAuthenticated });

  if (!isAuthenticated) return null;

  if (limitsQuery.isPending) {
    return <Skeleton className="h-44 w-full rounded-xl" />;
  }
  if (limitsQuery.isError || !limitsQuery.data) return null;

  const limits = limitsQuery.data;
  const proGrant = findTrainerProGrant(grantsQuery.data ?? []);
  const days = proGrant ? daysLeft(proGrant.expiresAt) : null;

  return (
    <section className="rounded-xl border border-border/60 bg-card/70 p-4 shadow-sm">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-1.5 text-sm font-semibold tracking-tight">
          <Icons.energy
            className={cn("size-4", limits.isPro ? "text-violet-500 dark:text-violet-300" : "text-muted-foreground")}
          />
          Тренажёр Pro
        </h2>
        <span
          className={cn(
            "rounded-md px-1.5 py-0.5 text-[11px] font-medium",
            limits.isPro
              ? "bg-violet-500/12 text-violet-600 dark:text-violet-300"
              : "bg-muted text-muted-foreground",
          )}
        >
          {limits.isPro ? "Активна" : "Free"}
        </span>
      </div>

      <p className="mb-3 text-xs text-muted-foreground">{statusLine(limits.isPro, proGrant, days)}</p>

      {/* У free-юзера ВСЕ AI-фичи (голос, мок, разбор открытых) за подпиской — показываем апселл,
          а не остаток лимитов (он для них неактуален). У PRO — остаток по каждому измерению. */}
      {limits.isPro ? (
        <div className="space-y-2.5">
          <LimitRow label="Проверки в день" limit={limits.openGrades} />
          <LimitRow label="Голос, мин / мес" limit={limits.voice} unit="мин" />
          <LimitRow label="Мок-собесы / мес" limit={limits.mock} />
        </div>
      ) : (
        <Link
          href={routes.trainerPro}
          className="inline-flex items-center gap-1 text-xs font-medium text-violet-600 hover:underline dark:text-violet-300"
        >
          Оформить Pro
          <Icons.chevronRight className="size-3.5" />
        </Link>
      )}
    </section>
  );
}

/** Строка одного лимита: «использовано / потолок» (+ unit, напр. «мин») + полоса; 0 ⇒ PRO-фича, null ⇒ безлимит. */
function LimitRow({ label, limit, unit }: { label: string; limit: TrainerLimit; unit?: string }) {
  const unlimited = limit.limit === null;
  const proOnly = limit.limit === 0;
  const percent = unlimited || proOnly ? 0 : Math.min(100, Math.round((limit.used / limit.limit!) * 100));
  const suffix = unit ? ` ${unit}` : "";

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between gap-2 text-xs">
        <span className="min-w-0 truncate text-muted-foreground">{label}</span>
        <span className="shrink-0 tabular-nums text-foreground/80">
          {unlimited ? "без лимита" : proOnly ? "только в Pro" : `${limit.used} / ${limit.limit}${suffix}`}
        </span>
      </div>
      {!unlimited && !proOnly && (
        <div className="h-1.5 overflow-hidden rounded-full bg-border/50">
          <div
            className={cn(
              "h-full rounded-full transition-[width]",
              percent >= 100 ? "bg-amber-500/80" : "bg-primary/70",
            )}
            style={{ width: `${Math.max(percent, 3)}%` }}
          />
        </div>
      )}
    </div>
  );
}

/** ACTIVE-грант, дающий Trainer Pro по подписке (capability TRAINER_PRO на плане). */
function findTrainerProGrant(grants: PlanGrantDto[]): PlanGrantDto | undefined {
  return grants.find(
    (g) => g.status === "ACTIVE" && g.plan != null && g.plan.capabilities.includes("TRAINER_PRO"),
  );
}

/** Дней до конца подписки (вверх); `null` если бессрочная / нет expiresAt. */
function daysLeft(expiresAt: string | null): number | null {
  if (!expiresAt) return null;
  const ms = new Date(expiresAt).getTime() - Date.now();
  return ms <= 0 ? 0 : Math.ceil(ms / 86_400_000);
}

/** Подпись статуса: подписка с днями / бессрочная / авто-PRO за полный доступ / free. */
function statusLine(isPro: boolean, proGrant: PlanGrantDto | undefined, days: number | null): string {
  if (!isPro) return "Голос, мок-собесы и AI-разбор открыты по подписке.";
  if (proGrant && days !== null) return `Подписка активна — ещё ${days} ${pluralize(days, "день", "дня", "дней")}.`;
  if (proGrant) return "Подписка активна (бессрочно).";
  return "Открыт за полный доступ к платформе.";
}
