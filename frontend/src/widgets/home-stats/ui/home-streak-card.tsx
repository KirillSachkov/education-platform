import type { ActivityDayDto, ActivityStreakDto } from "@/entities/user-activity";
import { buildRecentStrip } from "@/entities/user-activity";
import { cn } from "@/shared/lib/css";
import { formatRuPlural, pluralizeRu, RU_PLURALS } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { Card } from "@/shared/ui/kit/card";

/**
 * Карточка стрика для главной: крупное число текущей серии с огоньком + лучшая
 * серия + недельная полоска последних 7 дней (активные дни подсвечены). Серию и
 * флаг активности считает backend/lib — здесь только отображение.
 */
export function HomeStreakCard({
  streak,
  days,
}: {
  streak: ActivityStreakDto;
  days: ActivityDayDto[];
}) {
  const isActive = streak.current > 0;
  const strip = buildRecentStrip(days, 7);

  return (
    <Card className="p-5 gap-4">
      <div className="flex items-center gap-4">
        <div
          className={cn(
            "flex size-14 shrink-0 items-center justify-center rounded-full border",
            isActive
              ? "border-orange/30 bg-orange-dim text-orange"
              : "border-border/60 bg-muted text-muted-foreground",
          )}
        >
          <Icons.streak size={28} />
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex items-baseline gap-2">
            <span className="text-4xl font-bold tabular-nums tracking-tight">{streak.current}</span>
            <span className="text-sm text-muted-foreground">
              {pluralizeRu(streak.current, RU_PLURALS.day)} подряд
            </span>
          </div>
          <p className="mt-0.5 text-xs text-muted-foreground">
            лучшая серия: {formatRuPlural(streak.longest, RU_PLURALS.day)}
          </p>
        </div>
      </div>

      {strip.length > 0 && (
        <div className="flex items-end justify-between gap-1">
          {strip.map((d) => (
            <div key={d.date} className="flex flex-1 flex-col items-center gap-1">
              <span
                title={d.date}
                className={cn(
                  "size-3 rounded-full sm:size-3.5",
                  d.active ? "bg-orange" : "bg-muted",
                )}
              />
              <span className="text-[10px] leading-none text-muted-foreground">{d.weekday}</span>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
