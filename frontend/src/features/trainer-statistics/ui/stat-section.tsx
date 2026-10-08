"use client";

import type { CSSProperties, ReactNode } from "react";

import { TRAINER_CARD_SURFACE } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";

interface StatSectionProps {
  title: string;
  icon?: ReactNode;
  /** Контролы справа в шапке (например, переключатель периода). */
  action?: ReactNode;
  /** Пояснение под заголовком. */
  hint?: ReactNode;
  /** Индекс для каскадного входа (`--t-i`). */
  index?: number;
  className?: string;
  children: ReactNode;
}

/**
 * Секция-обёртка дашборда статистики (#568): материальная поверхность `.t-card`
 * + каскадный вход `.t-enter` + единая шапка (иконка-концепт + заголовок +
 * опц. контролы). Чтобы все блоки выглядели одинаково и не плодили свои карточки.
 */
export function StatSection({
  title,
  icon,
  action,
  hint,
  index = 0,
  className,
  children,
}: StatSectionProps) {
  return (
    <section
      className={cn(TRAINER_CARD_SURFACE, "t-enter p-4 sm:p-5", className)}
      style={{ "--t-i": index } as CSSProperties}
    >
      <header className="mb-4 flex items-center justify-between gap-3">
        <div className="flex min-w-0 items-center gap-2">
          {icon ? (
            <span className="shrink-0 text-muted-foreground/70" aria-hidden>
              {icon}
            </span>
          ) : null}
          <h2 className="truncate text-sm font-semibold tracking-tight">{title}</h2>
        </div>
        {action ?? null}
      </header>
      {hint ? <p className="-mt-2 mb-3 text-xs text-muted-foreground">{hint}</p> : null}
      {children}
    </section>
  );
}
