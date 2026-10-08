"use client";

import { useEffect, useRef, useState } from "react";

import { TRAINER_COUNT_UP_MS } from "@/shared/config/trainer";

const prefersReducedMotion = () =>
  typeof window !== "undefined" &&
  typeof window.matchMedia === "function" &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

interface CountUpOptions {
  /** Длительность рампа, мс. Default — `TRAINER_COUNT_UP_MS` (синхронно с заполнением колец). */
  durationMs?: number;
  /** Запускать рамп только когда true (например, при попадании карточки во вьюпорт). Default true. */
  enabled?: boolean;
  /** Знаков после запятой в выводе. Default 0. */
  decimals?: number;
}

/**
 * Рамп 0 → `target` через rAF с easeOutCubic (#568, дизайн-система тренажёра).
 * Отличается от `AnimatedNumber` (pop-in цифр при ИЗМЕНЕНИИ значения): этот хук
 * один раз «доезжает» снизу вверх при появлении — для reveal KPI на дашборде
 * статистики. Уважает `prefers-reduced-motion` (сразу финальное значение).
 */
export function useCountUp(target: number, options: CountUpOptions = {}): number {
  const { durationMs = TRAINER_COUNT_UP_MS, enabled = true, decimals = 0 } = options;
  const [value, setValue] = useState(0);
  const frameRef = useRef<number | null>(null);

  useEffect(() => {
    if (!enabled) return;

    if (prefersReducedMotion() || durationMs <= 0 || target === 0) {
      // Через rAF (не синхронно в эффекте) — сразу финал, без cascading-render лога.
      const instant = requestAnimationFrame(() => setValue(target));
      return () => cancelAnimationFrame(instant);
    }

    const factor = 10 ** decimals;
    let startTs: number | null = null;

    const tick = (ts: number) => {
      if (startTs === null) startTs = ts;
      const progress = Math.min((ts - startTs) / durationMs, 1);
      const eased = 1 - Math.pow(1 - progress, 3); // easeOutCubic — в тон --t-ease-out
      setValue(Math.round(target * eased * factor) / factor);
      if (progress < 1) frameRef.current = requestAnimationFrame(tick);
    };

    frameRef.current = requestAnimationFrame(tick);
    return () => {
      if (frameRef.current !== null) cancelAnimationFrame(frameRef.current);
    };
  }, [target, enabled, durationMs, decimals]);

  return value;
}
