"use client";

import type { CSSProperties } from "react";
import { cn } from "@/shared/lib/css";

interface SuccessCheckProps {
  /** Square SVG size in px. Default 16 (matches a `size-4` lucide icon). */
  size?: number;
  /**
   * Damp the bob / rotate / blur for a small inline icon (e.g. inside a button).
   * The full-strength motion is tuned for a large celebration glyph.
   */
  subtle?: boolean;
  className?: string;
}

// Dialed-down motion for inline use — keeps the stroke-draw + fade (the parts
// that read well at 16px) and trims the 40px bob that overshoots a button.
const SUBTLE_STYLE = {
  "--check-y-amount": "8px",
  "--check-rotate-from": "30deg",
  "--check-blur-from": "4px",
} as CSSProperties;

/**
 * transitions.dev "success check" (appear-only) — a checkmark that fades in,
 * rotates upright, un-blurs, Y-bobs, and draws its stroke. The animation plays
 * on mount, so render this component at the moment an action succeeds (e.g.
 * mount it only when a status flips to "done"); don't keep it mounted as a
 * static idle icon. Honors `prefers-reduced-motion` (shown instantly, no motion).
 *
 * stroke-dasharray is tuned to the `M5 13l4 4L19 7` path (~19.8u) in globals.css.
 */
export function SuccessCheck({ size = 16, subtle = false, className }: SuccessCheckProps) {
  return (
    <span
      className={cn("t-success-check", className)}
      data-state="in"
      style={subtle ? SUBTLE_STYLE : undefined}
      aria-hidden
    >
      <svg
        viewBox="0 0 24 24"
        width={size}
        height={size}
        fill="none"
        stroke="currentColor"
        strokeWidth={2.5}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M5 13l4 4L19 7" />
      </svg>
    </span>
  );
}
