import { cn } from "@/shared/lib/css";
import type { ContentAccessType } from "./access-type-selector";

interface AccessTypeBadgeProps {
  accessType: ContentAccessType;
  /** `inline` — compact pill (admin lists, cards). `solid` — bigger chip. */
  size?: "inline" | "solid";
  className?: string;
}

const ACCESS_LABELS: Record<ContentAccessType, string> = {
  PUBLIC: "Публичная",
  REGISTERED: "Авторизованным",
  ENROLLED: "Записанным",
};

const ACCESS_CLASSES: Record<ContentAccessType, string> = {
  PUBLIC: "border-border/50 bg-transparent text-muted-foreground/70",
  REGISTERED: "border-sky-500/30 bg-sky-500/10 text-sky-400",
  ENROLLED: "border-purple-500/30 bg-purple-500/10 text-purple-400",
};

/**
 * Small outline pill indicating the AccessType of a content item.
 * Single source of truth for the student-facing access labels.
 */
export function AccessTypeBadge({
  accessType,
  size = "inline",
  className,
}: AccessTypeBadgeProps) {
  return (
    <span
      className={cn(
        "rounded-md border font-medium",
        size === "inline"
          ? "px-1.5 py-0.5 text-[10px]"
          : "px-2 py-0.5 text-xs",
        ACCESS_CLASSES[accessType],
        className,
      )}
    >
      {ACCESS_LABELS[accessType]}
    </span>
  );
}
