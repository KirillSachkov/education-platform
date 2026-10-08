import type { ComponentType } from "react";
import type { ActivityTotalsDto } from "@/entities/user-activity";
import { Icons } from "@/shared/ui/icons";

interface Tile {
  label: string;
  value: number;
  icon: ComponentType<{ className?: string }>;
}

/**
 * Три KPI-плитки за всё время: изучено уроков, принято заданий, накоплено XP —
 * прямой ответ на «сколько всего заданий выполнено / уроков просмотрено».
 * Цифры из `GET /progress/my/activity` (totals).
 */
export function KpiTiles({ totals }: { totals: ActivityTotalsDto }) {
  const tiles: Tile[] = [
    { label: "Уроков изучено", value: totals.materialsCompleted, icon: Icons.view },
    { label: "Заданий принято", value: totals.issuesApproved, icon: Icons.checkAll },
    { label: "Всего XP", value: totals.totalXp, icon: Icons.xp },
  ];

  return (
    <div className="grid grid-cols-3 gap-2 sm:gap-4">
      {tiles.map(({ label, value, icon: Icon }) => (
        <div key={label} className="rounded-xl border p-3 sm:p-4">
          <Icon className="size-4 text-muted-foreground" />
          <div className="mt-2 text-xl font-bold tabular-nums sm:text-2xl">
            {value.toLocaleString("ru-RU")}
          </div>
          <div className="mt-0.5 text-xs text-muted-foreground sm:text-sm">{label}</div>
        </div>
      ))}
    </div>
  );
}
