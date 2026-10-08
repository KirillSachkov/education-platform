"use client";

import type { CSSProperties, ReactNode } from "react";

import { TRAINER_CARD_SURFACE } from "@/shared/config/trainer";
import { useCountUp } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";

interface StatCardProps {
  label: string;
  /** Числовое значение — анимируется count-up'ом при появлении. */
  value: number;
  /** Например `"%"`. */
  suffix?: string;
  icon?: ReactNode;
  /** Подпись под значением (контекст/тренд). */
  footer?: ReactNode;
  /** Класс акцента иконки (`text-green` / `text-amber-500` / …). По умолчанию приглушённая. */
  accentClass?: string;
  /** Индекс для каскадного входа (`--t-i`). */
  index?: number;
  decimals?: number;
}

/**
 * Материальная KPI-карточка дашборда статистики (#568): иконка-концепт +
 * подпись + крупное значение с count-up рампом + опц. футер. Поверхность —
 * `.t-card` + каскадный вход `.t-enter`. Иконку бери из `TRAINER_STAT_ICONS`,
 * чтобы стат везде обозначался одинаково.
 */
export function StatCard({
  label,
  value,
  suffix,
  icon,
  footer,
  accentClass,
  index = 0,
  decimals = 0,
}: StatCardProps) {
  const display = useCountUp(value, { decimals });

  return (
    <div
      className={cn(TRAINER_CARD_SURFACE, "t-enter flex flex-col gap-3 p-4 sm:p-5")}
      style={{ "--t-i": index } as CSSProperties}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
          {label}
        </span>
        {icon ? (
          <span className={cn("text-muted-foreground/70", accentClass)} aria-hidden>
            {icon}
          </span>
        ) : null}
      </div>
      <p className="text-3xl leading-none font-semibold tabular-nums">
        {display}
        {suffix ? <span className="text-2xl text-muted-foreground">{suffix}</span> : null}
      </p>
      {footer ? <div className="text-xs text-muted-foreground">{footer}</div> : null}
    </div>
  );
}
