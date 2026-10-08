"use client";

import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { useEffect, useState } from "react";

interface SessionTimerProps {
  /** Старт сессии (ISO) — отсчёт идёт от него. */
  startedAt: string;
  /** Лимит в секундах. */
  limitSeconds: number;
  /** Сессия завершена — таймер замораживается. */
  frozen?: boolean;
  /**
   * Колбэк смены «истёк ли таймер» (#614 E). Раннер использует это для
   * hard-stop MOCK-сессии (блокирует ввод/запись на нуле). Вызывается на каждый
   * переход false↔true.
   */
  onExpiredChange?: (expired: boolean) => void;
}

/** Порог «скоро конец» (амбер) и «вот-вот» (красный + пульс), в секундах. */
const WARN_THRESHOLD_SECONDS = 60;
const CRITICAL_THRESHOLD_SECONDS = 15;

/**
 * Обратный отсчёт MOCK-сессии (#568, hard-stop #614 E). Считает оставшееся
 * время от `startedAt + limitSeconds` и проходит уровни срочности:
 *  - `>60с` — нейтральный;
 *  - `≤60с` — амбер «Скоро конец»;
 *  - `≤15с` — красный + лёгкий пульс (функциональный сигнал срочности, с
 *    `prefers-reduced-motion`-гардом в globals.css);
 *  - `0` — красный «Время вышло».
 * На нуле сообщает раннеру через `onExpiredChange`, чтобы тот заблокировал ответ
 * (сервер не авто-фейлит — завершает пользователь). Тикает раз в секунду.
 */
export function SessionTimer({ startedAt, limitSeconds, frozen, onExpiredChange }: SessionTimerProps) {
  const deadline = new Date(startedAt).getTime() + limitSeconds * 1000;
  const [remaining, setRemaining] = useState(() => computeRemaining(deadline));

  useEffect(() => {
    if (frozen) return;
    const tick = () => setRemaining(computeRemaining(deadline));
    tick();
    const interval = setInterval(tick, 1000);
    return () => clearInterval(interval);
  }, [deadline, frozen]);

  const isUp = remaining <= 0;
  const isCritical = !isUp && remaining <= CRITICAL_THRESHOLD_SECONDS;
  const isWarn = !isUp && !isCritical && remaining <= WARN_THRESHOLD_SECONDS;

  // Уведомляем раннер о смене expired-состояния (для hard-stop). Не в render —
  // через эффект, чтобы не дёргать setState родителя во время рендера.
  useEffect(() => {
    onExpiredChange?.(isUp);
  }, [isUp, onExpiredChange]);

  return (
    <span
      role="timer"
      aria-live={isWarn || isCritical ? "polite" : "off"}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-sm font-medium tabular-nums",
        isUp || isCritical
          ? "border-destructive/40 bg-destructive/10 text-destructive"
          : isWarn
            ? "border-amber-500/40 bg-amber-500/10 text-amber-600 dark:text-amber-400"
            : "border-border/60 bg-card/50 text-muted-foreground",
        isCritical && "t-timer-pulse",
      )}
    >
      <Icons.clock className="size-3.5 shrink-0" />
      {isUp ? "Время вышло" : isWarn ? `Скоро конец · ${formatClock(remaining)}` : formatClock(remaining)}
    </span>
  );
}

function computeRemaining(deadline: number): number {
  return Math.max(0, Math.round((deadline - Date.now()) / 1000));
}

function formatClock(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}
