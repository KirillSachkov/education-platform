import type { ActivityStreakDto } from "@/entities/user-activity";
import { cn } from "@/shared/lib/css";
import { formatRuPlural, pluralizeRu, RU_PLURALS } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { Card } from "@/shared/ui/kit/card";

/**
 * Карточка стрика: крупное число текущей серии с огоньком + лучшая серия.
 * Серия не рвётся, пока не закончится день без активности (grace-day) —
 * семантику считает backend, тут только отображение.
 */
export function StreakCard({ streak }: { streak: ActivityStreakDto }) {
  const isActive = streak.current > 0;

  return (
    <Card className="p-5 gap-0">
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

        <div className="min-w-0">
          <div className="flex items-baseline gap-2">
            <span className="text-4xl font-bold tabular-nums tracking-tight">
              {streak.current}
            </span>
            <span className="text-sm text-muted-foreground">
              {pluralizeRu(streak.current, RU_PLURALS.day)} подряд
            </span>
          </div>
          <p className="mt-0.5 text-xs text-muted-foreground">
            лучшая серия: {formatRuPlural(streak.longest, RU_PLURALS.day)}
          </p>
        </div>
      </div>
    </Card>
  );
}
