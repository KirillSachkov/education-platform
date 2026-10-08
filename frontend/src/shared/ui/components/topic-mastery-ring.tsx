import { getMasteryStrokeTone } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";

/**
 * Mastery-кольцо темы (Duolingo-приём, #568/#585): SVG-ринг + крупный % по
 * центру, цвет по порогам (`getMasteryStrokeTone`: зелёный ≥80 / primary 60-79 /
 * янтарь <60). Общий визуальный язык освоения для хаба, пикера темы и дашборда
 * статистики — пороги/размеры не дрейфят между экранами (живёт в `shared/ui`,
 * чтобы и widgets, и features тянули один компонент без upward-импорта).
 *
 * `variant="new"` — тема ещё не тронута (0 ответов): спокойное тонкое
 * low-contrast кольцо без прогресс-дуги, по центру — мягкое «Начать ›» вместо
 * процента (тихое приглашение, не «грустный пустой кружок на 0%»).
 *
 * SVG детерминирован по `percent`/`variant`/`size` — на hover родителя НИЧЕГО
 * не меняется и не пере-маунтится, поэтому ринг не дёргается при наведении.
 */
export function TopicMasteryRing({
  percent,
  variant = "progress",
  size = 64,
}: {
  percent: number;
  variant?: "progress" | "new";
  size?: number;
}) {
  const isNew = variant === "new";
  const strokeWidth = isNew ? 3 : 6;
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const safePercent = Math.max(0, Math.min(100, percent));
  const offset = circumference * (1 - safePercent / 100);

  return (
    <div className="relative inline-flex shrink-0 items-center justify-center">
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        className="-rotate-90"
        role="progressbar"
        aria-valuenow={isNew ? 0 : safePercent}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          strokeWidth={strokeWidth}
          className={isNew ? "stroke-border/70" : "stroke-muted-foreground/15"}
        />
        {!isNew && (
          <circle
            cx={size / 2}
            cy={size / 2}
            r={radius}
            fill="none"
            strokeWidth={strokeWidth}
            strokeLinecap="round"
            strokeDasharray={circumference}
            strokeDashoffset={offset}
            className={cn(
              "transition-[stroke-dashoffset] duration-700",
              getMasteryStrokeTone(safePercent),
            )}
          />
        )}
      </svg>
      <span
        className={cn(
          "absolute inset-0 grid place-items-center tabular-nums",
          isNew ? "text-[11px] font-medium text-muted-foreground/80" : "text-sm font-bold",
        )}
      >
        {isNew ? "Начать ›" : `${safePercent}%`}
      </span>
    </div>
  );
}
