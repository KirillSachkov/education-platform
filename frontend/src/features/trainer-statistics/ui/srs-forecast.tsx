"use client";

import Link from "next/link";

import type { TrainerStatsSrs } from "@/entities/trainer-stats";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

const DAY_MS = 86_400_000;
const weekdayLabel = new Intl.DateTimeFormat("ru", { weekday: "short", timeZone: "UTC" });

/** Следующие 7 UTC-дней (с завтрашнего) — ключи дат под `upcoming` бэкенда. */
function nextSevenDays(): { key: string; ts: number }[] {
  const now = new Date();
  const todayUtc = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  return Array.from({ length: 7 }, (_, i) => {
    const ts = todayUtc + (i + 1) * DAY_MS;
    return { key: new Date(ts).toISOString().slice(0, 10), ts };
  });
}

/**
 * Блок 7 — SRS-прогноз (#568): «на повтор сегодня» с CTA + столбики на 7 дней
 * вперёд (когда подойдут повторы) + retention (Знаю/Видел). Возвращает юзера в
 * тренажёр.
 */
export function SrsForecast({ srs }: { srs: TrainerStatsSrs }) {
  const dueByDate = new Map(srs.upcoming.map((u) => [u.date, u.due]));
  const days = nextSevenDays();
  const maxDue = Math.max(1, ...days.map((d) => dueByDate.get(d.key) ?? 0));

  return (
    <StatSection
      title="Повторение (SRS)"
      icon={<TrainerStatIcon concept="due" className="size-4" />}
      index={6}
      action={
        <span className="text-xs text-muted-foreground tabular-nums">
          удержание <span className="font-semibold text-foreground/80">{srs.retentionPercent}%</span>
        </span>
      }
    >
      <div className="flex items-center justify-between gap-4">
        <div>
          <p
            className={cn(
              "text-3xl font-bold tabular-nums",
              srs.dueToday > 0 ? "text-amber-600 dark:text-amber-400" : "text-foreground",
            )}
          >
            {srs.dueToday}
          </p>
          <p className="text-xs text-muted-foreground">на повтор сегодня</p>
        </div>
        {srs.dueToday > 0 && (
          <Button asChild size="sm">
            <Link href={routes.trainer}>Повторить</Link>
          </Button>
        )}
      </div>

      <div className="mt-5">
        <div className="flex items-end justify-between gap-1.5" style={{ height: 64 }}>
          {days.map((d) => {
            const due = dueByDate.get(d.key) ?? 0;
            const heightPct = due > 0 ? Math.max((due / maxDue) * 100, 8) : 0;
            return (
              <div key={d.key} className="flex flex-1 flex-col items-center justify-end gap-1">
                <div
                  className="w-full rounded-t-sm bg-primary/70"
                  style={{ height: `${heightPct}%` }}
                  title={`${due} на повтор`}
                  aria-hidden
                />
              </div>
            );
          })}
        </div>
        <div className="mt-1.5 flex justify-between gap-1.5">
          {days.map((d) => (
            <span
              key={d.key}
              className="flex-1 text-center text-[10px] text-muted-foreground capitalize"
            >
              {weekdayLabel.format(new Date(d.ts))}
            </span>
          ))}
        </div>
      </div>
    </StatSection>
  );
}
