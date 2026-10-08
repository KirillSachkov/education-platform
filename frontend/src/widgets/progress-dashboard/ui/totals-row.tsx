import type { ActivityTotalsDto } from "@/entities/user-activity";

/**
 * Три итоговые карточки за всё время. Повторяет StatCard-идиому страницы
 * прогресса курса (/courses/[slug]/progress).
 */
export function TotalsRow({ totals }: { totals: ActivityTotalsDto }) {
  const items = [
    { label: "Всего XP", value: totals.totalXp },
    { label: "Материалов изучено", value: totals.materialsCompleted },
    { label: "Заданий принято", value: totals.issuesApproved },
  ];

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
      {items.map((item) => (
        <div key={item.label} className="rounded-xl border p-4">
          <div className="text-2xl font-bold tabular-nums">
            {item.value.toLocaleString("ru-RU")}
          </div>
          <div className="mt-1 text-sm text-muted-foreground">{item.label}</div>
        </div>
      ))}
    </div>
  );
}
