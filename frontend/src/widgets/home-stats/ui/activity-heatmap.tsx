import type { ActivityDayDto } from "@/entities/user-activity";
import { ACTIVITY_INTENSITY_CLASSES, activityIntensityIndex, dayScore } from "@/entities/user-activity";
import { cn } from "@/shared/lib/css";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { Card } from "@/shared/ui/kit/card";

const DAY_LABEL_FORMAT = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "short",
});

function dayTitle(day: ActivityDayDto): string {
  // "yyyy-MM-dd" парсим как локальную полночь — голая дата трактуется как
  // UTC-полночь и в западных таймзонах сместила бы подпись на день назад.
  const label = DAY_LABEL_FORMAT.format(new Date(`${day.date}T00:00:00`));
  return `${label}: ${day.xp} XP, ${formatRuPlural(day.materialsCompleted, RU_PLURALS.material)}`;
}

/**
 * GitHub-style heatmap активности: 12 недель × 7 дней. Колонка — 7
 * последовательных дней сверху вниз (grid-flow-col + grid-rows-7), старые слева,
 * сегодня — правый нижний угол. На узких экранах — горизонтальный скролл.
 * Подпись (title) даёт не-цветовой канал для доступности.
 */
export function ActivityHeatmap({ days }: { days: ActivityDayDto[] }) {
  return (
    <Card className="p-5 gap-4">
      <h3 className="text-sm font-medium">Активность за 12 недель</h3>

      <div className="overflow-x-auto">
        <div className="grid w-max grid-flow-col grid-rows-7 gap-1">
          {days.map((day) => (
            <div
              key={day.date}
              title={dayTitle(day)}
              className={cn(
                "size-3.5 rounded-[3px] sm:size-4",
                ACTIVITY_INTENSITY_CLASSES[activityIntensityIndex(dayScore(day))],
              )}
            />
          ))}
        </div>
      </div>

      <div className="flex items-center justify-end gap-1.5 text-xs text-muted-foreground">
        <span>Меньше</span>
        {ACTIVITY_INTENSITY_CLASSES.map((levelClass) => (
          <span key={levelClass} className={cn("size-3 rounded-[3px]", levelClass)} />
        ))}
        <span>Больше</span>
      </div>
    </Card>
  );
}
