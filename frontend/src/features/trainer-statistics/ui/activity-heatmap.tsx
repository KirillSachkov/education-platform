"use client";

import type { TrainerActivityDay } from "@/entities/trainer-stats";
import { cn } from "@/shared/lib/css";

import { StatSection } from "./stat-section";
import { TrainerStatIcon } from "./trainer-stat-icon";

const DAY_MS = 86_400_000;
const WEEKS = 26; // ~6 месяцев — плитки тянутся на всю ширину карточки
const dateLabel = new Intl.DateTimeFormat("ru", { day: "numeric", month: "long" });

/** Бакет интенсивности дня по числу отвеченных вопросов → класс заливки (ramp green). */
function intensityClass(count: number): string {
  if (count <= 0) return "bg-muted/40";
  if (count <= 2) return "bg-green/25";
  if (count <= 5) return "bg-green/45";
  if (count <= 9) return "bg-green/70";
  return "bg-green";
}

interface HeatmapCell {
  key: string;
  ts: number;
  inFuture: boolean;
}

/** Сетка дней, выровненная по понедельникам, в UTC (как `started_at::date` на бэке). */
function buildCells(): HeatmapCell[] {
  const now = new Date();
  const todayUtc = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate());
  const todayDow = new Date(todayUtc).getUTCDay(); // 0=Вс..6=Сб
  const mondayOffset = (todayDow + 6) % 7;
  const lastMonday = todayUtc - mondayOffset * DAY_MS;
  const startMonday = lastMonday - (WEEKS - 1) * 7 * DAY_MS;

  const cells: HeatmapCell[] = [];
  for (let week = 0; week < WEEKS; week++) {
    for (let day = 0; day < 7; day++) {
      const ts = startMonday + (week * 7 + day) * DAY_MS;
      cells.push({
        key: new Date(ts).toISOString().slice(0, 10),
        ts,
        inFuture: ts > todayUtc,
      });
    }
  }
  return cells;
}

/**
 * Блок 2 — календарь активности (#568): heatmap ~13 недель (Duolingo/GitHub-стиль),
 * интенсивность ячейки = отвечено вопросов в этот день. Питается дневной серией
 * `TrainerActivity.days`. Подпись — текущая/рекордная серия.
 */
export function ActivityHeatmap({
  days,
  currentStreak,
  longestStreak,
}: {
  days: TrainerActivityDay[];
  currentStreak: number;
  longestStreak: number;
}) {
  const countByDate = new Map(days.map((d) => [d.date, d.answered]));
  const cells = buildCells();

  return (
    <StatSection
      title="Активность"
      icon={<TrainerStatIcon concept="activity" className="size-4" />}
      index={1}
      action={
        <span className="text-xs text-muted-foreground tabular-nums">
          серия <span className="font-semibold text-foreground/80">{currentStreak}</span> · рекорд{" "}
          {longestStreak}
        </span>
      }
    >
      <div>
        <div
          className="grid w-full auto-cols-fr grid-flow-col grid-rows-7 gap-[3px]"
          role="img"
          aria-label={`Активность за ${WEEKS} недель, серия ${currentStreak} дней`}
        >
          {cells.map((cell) => {
            if (cell.inFuture) {
              return <span key={cell.key} className="aspect-square rounded-[4px]" aria-hidden />;
            }
            const count = countByDate.get(cell.key) ?? 0;
            return (
              <span
                key={cell.key}
                className={cn("aspect-square rounded-[4px]", intensityClass(count))}
                title={
                  count > 0
                    ? `${dateLabel.format(new Date(cell.ts))}: ${count} вопросов`
                    : `${dateLabel.format(new Date(cell.ts))}: нет активности`
                }
              />
            );
          })}
        </div>
      </div>

      <div className="mt-3 flex items-center gap-1.5 text-[11px] text-muted-foreground">
        <span>меньше</span>
        <span className="size-3 rounded-[3px] bg-muted/40" />
        <span className="size-3 rounded-[3px] bg-green/25" />
        <span className="size-3 rounded-[3px] bg-green/45" />
        <span className="size-3 rounded-[3px] bg-green/70" />
        <span className="size-3 rounded-[3px] bg-green" />
        <span>больше</span>
      </div>
    </StatSection>
  );
}
