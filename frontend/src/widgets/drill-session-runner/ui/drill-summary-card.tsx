"use client";

import type { TrainerSessionSummary } from "@/entities/trainer-session";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";

interface DrillSummaryCardProps {
  summary: TrainerSessionSummary;
  backHref?: string;
  /** Режим сессии — задаёт копирайт итога: DRILL → «Тест», LEARN → «Тренировка». */
  mode: string;
}

/**
 * Итог завершённой DRILL/LEARN-сессии (#568): крупный балл + счётчики (отвечено / верно)
 * + CTA «К теме» / «Мой прогресс». Полный разбор ответов рендерится отдельным блоком под
 * этой карточкой. Балл — доля верных среди всех авто-грейдимых вопросов; открытые
 * (самопроверка) в знаменатель не входят.
 */
export function DrillSummaryCard({ summary, backHref, mode }: DrillSummaryCardProps) {
  const strong = summary.scorePercent >= 70;
  const isTraining = mode === "LEARN";
  const heading = isTraining
    ? strong
      ? "Отличная тренировка!"
      : "Тренировка завершена"
    : strong
      ? "Тест пройден!"
      : "Тест завершён";

  return (
    <div className="mx-auto w-full max-w-xl space-y-5">
      <div className="overflow-hidden rounded-xl border border-border/60 bg-card">
        <div
          className={cn(
            "flex flex-col items-center gap-1.5 px-6 py-9 text-center",
            strong ? "bg-green/5" : "bg-muted/30",
          )}
        >
          <p className="text-5xl font-semibold tabular-nums">{summary.scorePercent}%</p>
          <p className="text-sm text-muted-foreground">{heading}</p>
        </div>

        <dl className="grid grid-cols-3 divide-x divide-border/60 border-t border-border/60 text-center">
          <Stat label="Вопросов" value={summary.totalItems} />
          <Stat label="Отвечено" value={summary.answeredItems} />
          <Stat label="Верно" value={summary.correctItems} />
        </dl>
      </div>

      <div className="flex flex-col gap-2 sm:flex-row">
        <Button asChild variant="outline" className="flex-1">
          <Link href={routes.trainerProgress}>Мой прогресс</Link>
        </Button>
        <Button asChild className="flex-1">
          <Link href={backHref ?? routes.trainer}>К теме</Link>
        </Button>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="px-2 py-4">
      <dd className="text-xl font-semibold tabular-nums">{value}</dd>
      <dt className="text-xs text-muted-foreground">{label}</dt>
    </div>
  );
}
