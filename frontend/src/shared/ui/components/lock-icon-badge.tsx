import { type LockReason, resolveLockCopy } from "@/shared/lib/lock-copy";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";

interface LockIconBadgeProps {
  reason: LockReason | null | undefined;
  /** Optional course title — only used when the tooltip wants to name the course. */
  courseTitle?: string | null;
  /**
   * `overlay` — positioned absolute (top-right corner on cover). Default.
   * `inline` — rendered inline inside a flex row.
   */
  variant?: "overlay" | "inline";
  className?: string;
}

/**
 * Small square badge with a lock icon, used on content covers/rows when
 * there is no space for a full {@link LockCallout}. Tooltip = short hint.
 */
export function LockIconBadge({
  reason,
  courseTitle,
  variant = "overlay",
  className,
}: LockIconBadgeProps) {
  const { shortHint } = resolveLockCopy(reason, courseTitle);
  return (
    <div
      className={cn(
        "flex items-center justify-center rounded-md bg-black/60 text-white/90 backdrop-blur-sm",
        variant === "overlay" ? "absolute right-2 top-2 size-6" : "size-5",
        className,
      )}
      title={shortHint}
      aria-label={shortHint}
    >
      <Icons.locked className="size-3" />
    </div>
  );
}
