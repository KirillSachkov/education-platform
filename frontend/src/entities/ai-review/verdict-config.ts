import type { AiReviewVerdict } from "./types";

/**
 * Single source of truth for AI-verdict presentation (label + colour tone).
 * Lives in entities/ so both the student widget and the author history slice
 * read from the same map without crossing the FSD boundary.
 *
 * Tone classes use the platform semantic palette (`green`/`yellow`/`red` +
 * `-dim` soft fill / `-muted` border) declared in globals.css.
 */
export const VERDICT_LABEL: Record<AiReviewVerdict, string> = {
  LOOKS_GOOD: "Принято",
  MINOR_ISSUES: "Незначительные замечания",
  MAJOR_ISSUES: "Серьёзные замечания",
  OFF_TOPIC: "Не по теме",
};

export interface VerdictTone {
  /** Pill: border + soft fill + text. */
  pill: string;
  /** Solid accent text (e.g. timeline dot, headings). */
  text: string;
  /** Timeline dot ring colour. */
  dot: string;
}

export const VERDICT_TONE: Record<AiReviewVerdict, VerdictTone> = {
  LOOKS_GOOD: {
    pill: "border-green-muted bg-green-dim text-green",
    text: "text-green",
    dot: "bg-green",
  },
  MINOR_ISSUES: {
    pill: "border-yellow-muted bg-yellow-dim text-yellow",
    text: "text-yellow",
    dot: "bg-yellow",
  },
  MAJOR_ISSUES: {
    pill: "border-red-muted bg-red-dim text-red",
    text: "text-red",
    dot: "bg-red",
  },
  OFF_TOPIC: {
    pill: "border-muted-foreground/30 bg-muted/50 text-muted-foreground",
    text: "text-muted-foreground",
    dot: "bg-muted-foreground/60",
  },
};
