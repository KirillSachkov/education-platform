"use client";

import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";

/**
 * Chip-style toggle «Только бесплатное». State снаружи (page управляет URL `?free=1`),
 * компонент — только UI. Иконка lock-open подчёркивает «открытое содержимое».
 */
export function FreeOnlyToggle({
  active,
  onChange,
  className,
  label = "Только бесплатное",
}: {
  active: boolean;
  onChange: (next: boolean) => void;
  className?: string;
  label?: string;
}) {
  return (
    <button
      type="button"
      onClick={() => onChange(!active)}
      aria-pressed={active}
      className={cn(
        "inline-flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium border transition-colors whitespace-nowrap",
        active
          ? "border-emerald-500/70 bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 shadow-[0_0_0_1px_rgba(16,185,129,0.25)]"
          : "border-emerald-500/30 bg-card text-emerald-700/85 dark:text-emerald-400/85 hover:bg-emerald-500/5 hover:border-emerald-500/50 hover:text-emerald-700 dark:hover:text-emerald-300",
        className,
      )}
    >
      <Icons.unlocked size={13} />
      {label}
    </button>
  );
}
