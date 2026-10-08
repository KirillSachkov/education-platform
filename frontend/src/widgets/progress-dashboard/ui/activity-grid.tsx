import type { ActivityDayDto } from "@/entities/user-activity";
import { cn } from "@/shared/lib/css";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { Card } from "@/shared/ui/kit/card";

const DAY_LABEL_FORMAT = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "short",
});

const INTENSITY_CLASSES = ["bg-muted", "bg-primary/30", "bg-primary/60", "bg-primary"] as const;

/**
 * Квантование активности дня (XP + материалы) в 4 уровня интенсивности.
 * Пороги подобраны под XP-номиналы платформы: один изученный материал
 * (10 XP + 1) — уровень 1, несколько — уровень 2, день с модулем/проектом
 * (50+ XP) — уровень 3.
 */
function intensityClass(score: number): string {
  if (score <= 0) return INTENSITY_CLASSES[0];
  if (score <= 15) return INTENSITY_CLASSES[1];
  if (score <= 45) return INTENSITY_CLASSES[2];
  return INTENSITY_CLASSES[3];
}

function dayTitle(day: ActivityDayDto): string {
  // "yyyy-MM-dd" парсим как локальную полночь — голая дата трактуется как
  // UTC-полночь и в западных таймзонах сместила бы подпись на день назад.
  const label = DAY_LABEL_FORMAT.format(new Date(`${day.date}T00:00:00`));
  return `${label}: ${day.xp} XP, ${formatRuPlural(day.materialsCompleted, RU_PLURALS.material)}`;
}

/**
 * GitHub-style сетка активности: 12 недель × 7 дней. Колонка — 7 последовательных
 * дней сверху вниз (grid-flow-col + grid-rows-7), старые слева, сегодня — правый
 * нижний угол. На мобильных — горизонтальный скролл.
 */
export function ActivityGrid({ days }: { days: ActivityDayDto[] }) {
  return (
    <Card className="p-5 gap-4">
      <h2 className="text-sm font-medium">Активность за 12 недель</h2>

      <div className="overflow-x-auto">
        <div className="grid w-max grid-flow-col grid-rows-7 gap-1">
          {days.map((day) => (
            <div
              key={day.date}
              title={dayTitle(day)}
              className={cn(
                "size-3.5 rounded-[3px] sm:size-4",
                intensityClass(day.xp + day.materialsCompleted),
              )}
            />
          ))}
        </div>
      </div>

      <div className="flex items-center justify-end gap-1.5 text-xs text-muted-foreground">
        <span>Меньше</span>
        {INTENSITY_CLASSES.map((levelClass) => (
          <span key={levelClass} className={cn("size-3 rounded-[3px]", levelClass)} />
        ))}
        <span>Больше</span>
      </div>
    </Card>
  );
}
