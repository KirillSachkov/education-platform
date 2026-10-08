"use client";

import type { CSSProperties, ReactNode } from "react";

import { getErrorMessage } from "@/shared/api";
import { TRAINER_CARD_SURFACE } from "@/shared/config/trainer";
import { useCountUp } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";

/** Рубли с 2 знаками + группировкой. */
export const rub = new Intl.NumberFormat("ru", { maximumFractionDigits: 2 });
/** Целые с группировкой. */
export const int = new Intl.NumberFormat("ru");
/** ₽ из микрорублей (₽×1e6). */
export const toRub = (micro: number) => micro / 1e6;
/** 0..1 доля → «NN%» (или «—» при null). */
export const pct = (rate: number | null | undefined) =>
  rate == null ? "—" : `${Math.round(rate * 100)}%`;

/** Человекочитаемые подписи режимов сессий тренажёра. */
export const MODE_LABELS: Record<string, string> = {
  DRILL: "Тренировка",
  LEARN: "Обучение",
  MOCK: "Мок-собес",
};

/** Подписи уровней сложности (зеркалят TRAINER_DIFFICULTY_VISUALS, но без бейджа-палитры). */
export const DIFFICULTY_LABELS: Record<string, string> = {
  JUNIOR: "Джуниор",
  MIDDLE: "Миддл",
  SENIOR: "Сеньор",
  UNSPECIFIED: "Без уровня",
};

interface AdminStatCardProps {
  label: string;
  /** Числовое значение — анимируется count-up'ом при появлении. */
  value: number;
  /** Форматтер итогового числа (default — целое с группировкой). */
  format?: (n: number) => string;
  suffix?: string;
  icon?: ReactNode;
  footer?: ReactNode;
  accentClass?: string;
  index?: number;
  decimals?: number;
}

/**
 * KPI-карточка админ-дашборда тренажёра (#681): зеркалит house-style `StatCard`
 * (`features/trainer-statistics`, cross-slice import запрещён FSD) — `.t-card` +
 * count-up ramp + иконка-концепт. Локальная копия в admin-слайсе.
 */
export function AdminStatCard({
  label,
  value,
  format = (n) => int.format(n),
  suffix,
  icon,
  footer,
  accentClass,
  index = 0,
  decimals = 0,
}: AdminStatCardProps) {
  const display = useCountUp(value, { decimals });

  return (
    <div
      className={cn(TRAINER_CARD_SURFACE, "t-enter flex flex-col gap-2 p-4")}
      style={{ "--t-i": index } as CSSProperties}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="text-2xs font-medium tracking-wide text-muted-foreground uppercase">
          {label}
        </span>
        {icon ? (
          <span className={cn("text-muted-foreground/70", accentClass)} aria-hidden>
            {icon}
          </span>
        ) : null}
      </div>
      <p className="text-2xl leading-none font-semibold tabular-nums">
        {format(display)}
        {suffix ? <span className="text-lg text-muted-foreground"> {suffix}</span> : null}
      </p>
      {footer ? <div className="text-xs text-muted-foreground">{footer}</div> : null}
    </div>
  );
}

interface AdminStatSectionProps {
  title: string;
  icon?: ReactNode;
  action?: ReactNode;
  hint?: ReactNode;
  index?: number;
  className?: string;
  children: ReactNode;
}

/** Секция-обёртка админ-дашборда (#681): `.t-card` + единая шапка. Зеркалит `StatSection`. */
export function AdminStatSection({
  title,
  icon,
  action,
  hint,
  index = 0,
  className,
  children,
}: AdminStatSectionProps) {
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
          <h3 className="truncate text-sm font-semibold tracking-tight">{title}</h3>
        </div>
        {action ?? null}
      </header>
      {hint ? <p className="-mt-2 mb-3 text-xs text-muted-foreground">{hint}</p> : null}
      {children}
    </section>
  );
}

/** Приглушённая строка «пусто» внутри секции/карточки. */
export function EmptyRow({ text }: { text: string }) {
  return <p className="py-6 text-center text-sm text-muted-foreground">{text}</p>;
}

/** Единый error-state вкладки админ-дашборда. */
export function StatsErrorState({ error }: { error: unknown }) {
  return (
    <EmptyState
      variant="card"
      icon={Icons.warning}
      title="Не удалось загрузить данные"
      description={getErrorMessage(error, "Повторите попытку позже")}
    />
  );
}

/** Скелет вкладки: KPI-ряд + два блока. */
export function TabSkeleton() {
  return (
    <div className="space-y-4">
      <section className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-[84px] rounded-xl" />
        ))}
      </section>
      <Skeleton className="h-64 rounded-xl" />
      <Skeleton className="h-56 rounded-xl" />
    </div>
  );
}
