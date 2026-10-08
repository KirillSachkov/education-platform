"use client";

import { cn } from "@/shared/lib/css";
import type { ReactNode } from "react";

export interface SegmentedOption<T extends string> {
  value: T;
  label: ReactNode;
  /** Опциональная иконка слева от подписи. */
  icon?: ReactNode;
}

interface SegmentedControlProps<T extends string> {
  options: SegmentedOption<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Подпись группы для скринридера. */
  ariaLabel: string;
  /** `pill` (по умолчанию, на приглушённом фоне) или `subtle` (компактнее, без подложки). */
  variant?: "pill" | "subtle";
  className?: string;
}

/**
 * Generic segmented-control (#568): набор взаимоисключающих опций как сегмент-табы.
 * Презентационный — состояние живёт у вызывающего. A11y: `role="group"` +
 * нативные `<button aria-pressed>` (selected-state не дублируется текстом).
 * Touch-таргеты ≥44px. Горизонтальный скролл при переполнении (mobile).
 * Не импортирует entities/features. Зеркалит идиому `CourseKindFilter`.
 */
export function SegmentedControl<T extends string>({
  options,
  value,
  onChange,
  ariaLabel,
  variant = "pill",
  className,
}: SegmentedControlProps<T>) {
  return (
    <div
      role="group"
      aria-label={ariaLabel}
      className={cn(
        "inline-flex max-w-full items-center gap-1 overflow-x-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
        variant === "pill" && "rounded-xl border bg-muted/50 p-1",
        className,
      )}
    >
      {options.map((option) => {
        const isActive = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={isActive}
            onClick={() => onChange(option.value)}
            className={cn(
              "inline-flex min-h-[44px] shrink-0 items-center gap-1.5 rounded-lg px-3 text-sm font-medium whitespace-nowrap transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
              isActive
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {option.icon}
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
