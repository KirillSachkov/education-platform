import type { CSSProperties } from "react";

/**
 * Лёгкая сезонная декорация хедера — несколько полупрозрачных листиков, мягко
 * дрейфующих в фоне. Чисто декоративная (aria-hidden, pointer-events:none).
 *
 * - CSS-only анимация (transform translate+rotate + opacity, GPU-friendly).
 * - Уважает `prefers-reduced-motion` (см. globals.css → `.animate-leaf-drift`).
 * - Month-gate: рендерится только летом (июнь–август), осенью сам исчезает —
 *   никакого cleanup-долга. Чтобы поменять сезон/выключить — правь `isSummer`.
 */

type Leaf = {
  left: string;
  top: string;
  size: number;
  color: string;
  opacity: number;
  /** Длительность одного цикла дрейфа. */
  dur: string;
  /** Отрицательный delay — рассинхронизирует листики, чтобы не дышали в такт. */
  delay: string;
};

// Приглушённая летняя палитра (тёмная тема — дефолт): зелень + один тёплый акцент.
const LEAVES: readonly Leaf[] = [
  { left: "5%", top: "16%", size: 16, color: "#34d399", opacity: 0.16, dur: "13s", delay: "0s" },
  { left: "19%", top: "56%", size: 12, color: "#4ade80", opacity: 0.13, dur: "16s", delay: "-4s" },
  { left: "37%", top: "10%", size: 14, color: "#2dd4bf", opacity: 0.14, dur: "11s", delay: "-7s" },
  { left: "57%", top: "52%", size: 18, color: "#a3e635", opacity: 0.12, dur: "15s", delay: "-2s" },
  { left: "75%", top: "20%", size: 13, color: "#fbbf24", opacity: 0.15, dur: "12s", delay: "-9s" },
  { left: "91%", top: "58%", size: 15, color: "#34d399", opacity: 0.13, dur: "14s", delay: "-5s" },
];

export function SeasonalHeaderDecor() {
  // Лето в северном полушарии: июнь(5) – август(7).
  const month = new Date().getMonth();
  const isSummer = month >= 5 && month <= 7;
  if (!isSummer) return null;

  return (
    <div aria-hidden className="pointer-events-none absolute inset-0 z-0 overflow-hidden">
      {LEAVES.map((leaf) => (
        <span
          key={`${leaf.left}-${leaf.top}`}
          className="animate-leaf-drift absolute"
          style={
            {
              left: leaf.left,
              top: leaf.top,
              color: leaf.color,
              opacity: leaf.opacity,
              "--leaf-dur": leaf.dur,
              "--leaf-delay": leaf.delay,
            } as CSSProperties
          }
        >
          <svg width={leaf.size} height={leaf.size} viewBox="0 0 24 24" fill="currentColor">
            <path d="M11 20A7 7 0 0 1 9.8 6.1C15.5 5 17 4.48 19 2c1 2 2 4.18 2 8 0 5.5-4.78 10-10 10Z" />
          </svg>
        </span>
      ))}
    </div>
  );
}
