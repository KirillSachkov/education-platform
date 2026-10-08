"use client";

import type { ReactNode } from "react";
import { cn } from "@/shared/lib/css";

export interface IconSwapItem {
  /** Stable identity for this icon slot. */
  key: string;
  node: ReactNode;
}

interface IconSwapProps {
  /** Key of the icon to show; the rest fade out with blur + scale. */
  activeKey: string;
  items: IconSwapItem[];
  className?: string;
}

/**
 * transitions.dev "icon swap" (generalised to N states) — stacks icons in one
 * grid cell and cross-fades to the active one. Handles 2-state (bookmark on/off)
 * and tri-state (theme light/dark/system) with one component. Pure CSS
 * transition, so it animates on change only — never on initial mount — and the
 * `prefers-reduced-motion` guard in globals.css disables the motion.
 */
export function IconSwap({ activeKey, items, className }: IconSwapProps) {
  return (
    <span className={cn("t-icon-swap", className)} aria-hidden>
      {items.map((item) => (
        <span key={item.key} className={cn("t-icon", item.key === activeKey && "is-active")}>
          {item.node}
        </span>
      ))}
    </span>
  );
}
