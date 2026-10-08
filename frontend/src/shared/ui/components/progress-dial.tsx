import { cn } from "@/shared/lib/css";

interface ProgressDialProps {
  percent: number;
  size?: number;
  strokeWidth?: number;
  label?: string;
  className?: string;
}

/**
 * Circular progress dial с inline-меткой процента.
 * Используется в шапках страниц курса (Программа / Задания) как единый
 * визуальный язык прогресса. 100% — зелёный, иначе primary.
 */
export function ProgressDial({
  percent,
  size = 60,
  strokeWidth = 5,
  label,
  className,
}: ProgressDialProps) {
  const r = (size - strokeWidth) / 2;
  const c = 2 * Math.PI * r;
  const safePercent = Math.max(0, Math.min(100, percent));
  const offset = c * (1 - safePercent / 100);
  const isComplete = safePercent === 100;
  const displayLabel = label ?? `${safePercent}%`;
  const textSize = size <= 40 ? "text-[10px]" : size <= 60 ? "text-[11px]" : "text-xs";

  return (
    <div className={cn("relative inline-flex items-center justify-center", className)}>
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        className="-rotate-90"
        aria-hidden="true"
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          strokeWidth={strokeWidth}
          className="stroke-muted-foreground/20"
        />
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={offset}
          className={cn(
            "transition-[stroke-dashoffset] duration-700",
            isComplete ? "stroke-green" : "stroke-primary",
          )}
        />
      </svg>
      <span
        className={cn(
          "absolute inset-0 grid place-items-center font-bold tabular-nums",
          textSize,
          isComplete ? "text-green" : "text-foreground",
        )}
      >
        {displayLabel}
      </span>
    </div>
  );
}
