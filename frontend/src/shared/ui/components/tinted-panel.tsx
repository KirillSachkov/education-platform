import type { ReactNode } from "react";

import { cn } from "@/shared/lib/css";

interface TintedPanelProps {
  /** `good` — зелёный (сильное), `warn` — янтарный (зона роста). */
  tone: "good" | "warn";
  title: string;
  icon?: ReactNode;
  /** Правый угол шапки (счётчик и т.п.). */
  meta?: ReactNode;
  className?: string;
  children: ReactNode;
}

/**
 * Тонированная панель-группа (#568): мягкий цветной фон + бордер + шапка с
 * иконкой/заголовком. Группирует чипы/строки в «сильные/слабые» блоках вместо
 * плоского списка — единый визуал в статистике и на странице симуляций.
 */
export function TintedPanel({ tone, title, icon, meta, className, children }: TintedPanelProps) {
  const surface =
    tone === "good" ? "border-green/20 bg-green/[0.05]" : "border-amber-500/20 bg-amber-500/[0.05]";
  const accent = tone === "good" ? "text-green" : "text-amber-600 dark:text-amber-400";

  return (
    <div className={cn("rounded-xl border p-4", surface, className)}>
      <div className="mb-3 flex items-center gap-1.5">
        {icon ? (
          <span className={accent} aria-hidden>
            {icon}
          </span>
        ) : null}
        <span className={cn("text-xs font-semibold tracking-wide uppercase", accent)}>{title}</span>
        {meta != null ? (
          <span className="ml-auto text-xs text-muted-foreground tabular-nums">{meta}</span>
        ) : null}
      </div>
      {children}
    </div>
  );
}
