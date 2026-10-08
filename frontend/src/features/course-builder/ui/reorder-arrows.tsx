"use client";

import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";

/**
 * Touch reorder fallback (recipe 7). `@dnd-kit/react` v10 pointer drag is janky
 * on touch, so on mobile we expose explicit up/down buttons that call the same
 * reorder mutation as the drag handle. Pointer drag stays for mouse users.
 *
 * `md:hidden` — desktop keeps the drag handle only. Each button is a ≥44px tap
 * target. Ends are disabled (can't move past the list boundary).
 */
interface ReorderArrowsProps {
  onMoveUp: () => void;
  onMoveDown: () => void;
  isFirst: boolean;
  isLast: boolean;
  /** Smaller variant for nested item rows. */
  compact?: boolean;
  className?: string;
}

export function ReorderArrows({
  onMoveUp,
  onMoveDown,
  isFirst,
  isLast,
  compact = false,
  className,
}: ReorderArrowsProps) {
  const iconSize = compact ? 14 : 16;
  return (
    <div className={cn("flex shrink-0 items-center md:hidden", className)}>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="size-11 min-h-[44px] min-w-[44px] text-muted-foreground disabled:opacity-30"
        onClick={onMoveUp}
        disabled={isFirst}
        aria-label="Переместить вверх"
      >
        <Icons.chevronUp size={iconSize} />
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="size-11 min-h-[44px] min-w-[44px] text-muted-foreground disabled:opacity-30"
        onClick={onMoveDown}
        disabled={isLast}
        aria-label="Переместить вниз"
      >
        <Icons.chevronDown size={iconSize} />
      </Button>
    </div>
  );
}
