import { cn } from "@/shared/lib/css";
import type { ComponentType, ReactNode } from "react";

type EmptyStateVariant = "plain" | "dashed" | "card";

type EmptyStateProps = {
  icon: ComponentType<{ className?: string }>;
  title: string;
  description?: string;
  action?: ReactNode;
  /**
   * `plain` — bare flex block (modals, simple states). Default.
   * `dashed` — dashed-border panel (inline feed/list empty).
   * `card` — solid Card-like panel (standalone page section).
   */
  variant?: EmptyStateVariant;
  className?: string;
};

const VARIANT_CLASSES: Record<EmptyStateVariant, string> = {
  plain: "min-h-[200px] py-8",
  dashed:
    "rounded-xl border border-dashed border-border/60 bg-card/40 py-12 px-4",
  card: "rounded-xl border border-border/60 bg-card py-10 px-6",
};

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  variant = "plain",
  className,
}: EmptyStateProps) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center gap-4 text-center",
        VARIANT_CLASSES[variant],
        className,
      )}
    >
      <Icon className="h-12 w-12 text-muted-foreground/50" />
      <div className="space-y-1">
        <p className="font-medium text-foreground">{title}</p>
        {description && (
          <p className="text-sm text-muted-foreground">{description}</p>
        )}
      </div>
      {action}
    </div>
  );
}
