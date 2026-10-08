"use client";

import type { CSSProperties } from "react";

import { getMasteryStrokeTone, TRAINER_CARD_SURFACE } from "@/shared/config/trainer";
import { useCountUp } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";

import { StatCard } from "./stat-card";
import { TrainerStatIcon } from "./trainer-stat-icon";

interface SummaryHeaderProps {
  /** Готовность к собесу: охват-взвешенный % по всем темам, либо null если практики ещё нет (#691). */
  readinessPercent: number | null;
  /** Сколько тем реально практиковалось (числитель «N из M тем»). */
  touchedTopics: number;
  /** Всего опубликованных тем (знаменатель «N из M тем»). */
  totalTopics: number;
  /** Выборка достаточна, чтобы доверять проценту. `false` → приглушённое «недостаточно данных». */
  isConfident: boolean;
  weakCount: number;
  currentStreak: number;
  longestStreak: number;
  totalAnswered: number;
  accuracyPercent: number;
  dueToday: number;
  studiedQuestions: number;
}

/**
 * Hero-кольцо готовности к собесу (#623, честная формула #691): дуга и число «доезжают»
 * вместе (rAF count-up). `percent === null` — практики ещё нет: кольцо приглушено, в центре «—».
 * `muted` (мало данных) — число показываем приглушённо и БЕЗ цветной дуги, чтобы не выдавать
 * шумную выборку за уверенный результат.
 */
function HeroRing({ percent, muted }: { percent: number | null; muted: boolean }) {
  const animated = useCountUp(percent ?? 0);
  const hasData = percent !== null;
  const showArc = hasData && !muted;
  const size = 128;
  const stroke = 10;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const safe = Math.max(0, Math.min(100, hasData ? animated : 0));
  const offset = circumference * (1 - safe / 100);

  return (
    <div className="relative inline-flex shrink-0 items-center justify-center">
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        className="-rotate-90"
        role="img"
        aria-label={
          !hasData
            ? "Готовность пока не определена"
            : muted
              ? `Готовность к собесу ${percent}% — недостаточно данных`
              : `Готовность к собесу ${percent}%`
        }
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          strokeWidth={stroke}
          className="stroke-muted-foreground/15"
        />
        {showArc && (
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            strokeWidth={stroke}
            strokeLinecap="round"
            strokeDasharray={circumference}
            strokeDashoffset={offset}
            className={getMasteryStrokeTone(percent)}
          />
        )}
      </svg>
      <span
        className={cn(
          "absolute inset-0 grid place-items-center text-3xl font-bold tabular-nums",
          (muted || !hasData) && "text-muted-foreground/70",
        )}
      >
        {hasData ? `${animated}%` : "—"}
      </span>
    </div>
  );
}

/**
 * Блок 1 — шапка-сводка дашборда (#568): hero-кольцо общего освоения + streak +
 * KPI-ряд (отвечено / точность / на повтор / изучено). Числа считаются вверх,
 * вход каскадом. Иконки — из единого реестра стат-концептов.
 */
export function SummaryHeader({
  readinessPercent,
  touchedTopics,
  totalTopics,
  isConfident,
  weakCount,
  currentStreak,
  longestStreak,
  totalAnswered,
  accuracyPercent,
  dueToday,
  studiedQuestions,
}: SummaryHeaderProps) {
  const lowConfidence = readinessPercent !== null && !isConfident;
  return (
    <div className="space-y-3">
      <section
        className={cn(
          TRAINER_CARD_SURFACE,
          "t-enter flex flex-col items-center gap-5 p-5 sm:flex-row sm:gap-7 sm:p-6",
        )}
        style={{ "--t-i": 0 } as CSSProperties}
      >
        <HeroRing percent={readinessPercent} muted={lowConfidence} />
        <div className="space-y-2 text-center sm:text-left">
          <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            Готовность к собесу
          </p>
          <p className="text-sm text-muted-foreground">
            {readinessPercent === null ? (
              "Пройди тесты, чтобы оценить готовность"
            ) : lowConfidence ? (
              <>
                <span className="font-medium">Недостаточно данных</span>
                {" · "}
                {touchedTopics} из {totalTopics} {pluralizeTopics(totalTopics)}
              </>
            ) : (
              <>
                {touchedTopics} из {totalTopics} {pluralizeTopics(totalTopics)}
                {weakCount > 0 && (
                  <>
                    {" · "}
                    <span className="font-medium text-amber-600 dark:text-amber-400">
                      {weakCount} подтянуть
                    </span>
                  </>
                )}
              </>
            )}
          </p>
          <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-500/10 px-3 py-1 text-sm font-semibold text-amber-600 dark:text-amber-400">
            <TrainerStatIcon concept="streak" className="size-4" />
            {currentStreak > 0
              ? `${currentStreak} ${pluralizeDays(currentStreak)} подряд`
              : "Начни серию сегодня"}
          </span>
        </div>
      </section>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard
          index={1}
          label="Отвечено"
          value={totalAnswered}
          icon={<TrainerStatIcon concept="answered" className="size-4" />}
        />
        <StatCard
          index={2}
          label="Точность"
          value={accuracyPercent}
          suffix="%"
          icon={<TrainerStatIcon concept="accuracy" className="size-4" />}
        />
        <StatCard
          index={3}
          label="На повтор сегодня"
          value={dueToday}
          icon={<TrainerStatIcon concept="due" className="size-4" />}
          accentClass={dueToday > 0 ? "text-amber-500" : undefined}
        />
        <StatCard
          index={4}
          label="Серия · рекорд"
          value={longestStreak}
          icon={<TrainerStatIcon concept="streak" className="size-4" />}
          footer={`изучено вопросов: ${studiedQuestions}`}
        />
      </div>
    </div>
  );
}

function pluralizeTopics(count: number): string {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return "тема";
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "темы";
  return "тем";
}

function pluralizeDays(count: number): string {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return "день";
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "дня";
  return "дней";
}
