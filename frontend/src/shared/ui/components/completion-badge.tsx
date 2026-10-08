import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";
import { formatNumericDate } from "@/shared/lib/date";

export type CompletionKind = "viewed" | "completed";

const LABELS: Record<CompletionKind, string> = {
  viewed: "Изучено",
  completed: "Выполнено",
};

interface CompletionBadgeProps {
  kind: CompletionKind;
  /** ISO date string. If omitted, badge renders without a date (compact form). */
  date?: string | null;
  className?: string;
  /** Hide the icon (useful in extra-tight rows). */
  iconHidden?: boolean;
}

/**
 * Small green pill rendered next to a material/issue title once the user has
 * viewed/completed it. Two forms:
 *   • with date  — "✓ Изучено 22.04.2026"  (full pill with bg)
 *   • date-less  — "✓ Изучено"             (text-only, used in dense lists)
 */
export function CompletionBadge({
  kind,
  date,
  className,
  iconHidden = false,
}: CompletionBadgeProps) {
  const label = LABELS[kind];

  if (date) {
    return (
      <span
        className={cn(
          "inline-flex items-center gap-1 rounded-full bg-emerald-500/10 dark:bg-emerald-400/10",
          "px-2 py-0.5 text-[11px] font-medium text-emerald-700 dark:text-emerald-300",
          "border border-emerald-500/20",
          className,
        )}
      >
        {!iconHidden && <Icons.completed className="size-3" />}
        <span>
          {label} <span className="tabular-nums">{formatNumericDate(date)}</span>
        </span>
      </span>
    );
  }

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 text-[10px] font-semibold",
        "text-emerald-600 dark:text-emerald-400",
        className,
      )}
      title={label}
    >
      {!iconHidden && <Icons.completed className="size-3" />}
      {label}
    </span>
  );
}

/**
 * Tailwind classes to apply to a title/preview when the entity is completed.
 * Returns `undefined` for non-completed state so callers can spread without checks.
 */
export function getCompletedTitleClass(isComplete: boolean): string | undefined {
  return isComplete ? "line-through text-muted-foreground/70" : undefined;
}

export function getCompletedDescriptionClass(
  isComplete: boolean,
): string | undefined {
  return isComplete ? "text-muted-foreground/60" : undefined;
}
