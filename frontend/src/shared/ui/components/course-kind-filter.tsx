"use client";

import { COURSE_KINDS, COURSE_KIND_LABELS, type CourseKind } from "@/shared/config/course-kind";
import { cn } from "@/shared/lib/css";

type FilterValue = CourseKind | "all";

interface CourseKindFilterOption {
  value: FilterValue;
  label: string;
}

/** «Все» + по одной опции на каждый CourseKind, в порядке COURSE_KINDS. */
const OPTIONS: CourseKindFilterOption[] = [
  { value: "all", label: "Все" },
  ...COURSE_KINDS.map((kind) => ({ value: kind, label: COURSE_KIND_LABELS[kind] })),
];

interface CourseKindFilterProps {
  value: FilterValue;
  onChange: (value: FilterValue) => void;
  className?: string;
}

/**
 * Презентационный segmented-control для фильтра по типу курса
 * (Все / Курсы / Интенсивы / Марафоны). Состояние живёт у вызывающего —
 * компонент только рисует и репортит выбор.
 *
 * A11y: контейнер — `role="group"` с подписью; каждая опция — нативный
 * `<button>` с `aria-pressed` (selected-state не дублируем в тексте).
 * Touch-таргеты ≥44px. Не импортирует entities/features.
 */
export function CourseKindFilter({ value, onChange, className }: CourseKindFilterProps) {
  return (
    <div
      role="group"
      aria-label="Фильтр по типу курса"
      className={cn(
        "inline-flex items-center gap-1 rounded-lg border bg-muted/50 p-1",
        className,
      )}
    >
      {OPTIONS.map((option) => {
        const isActive = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={isActive}
            onClick={() => onChange(option.value)}
            className={cn(
              "min-h-[44px] rounded-md px-3 text-sm font-medium whitespace-nowrap transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
              isActive
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
