import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";

/**
 * Компактный бейдж «N просмотров» с иконкой глаза. Источник —
 * <c>MaterialFeedItemDto.viewsCount / MaterialDetailDto.viewsCount</c>,
 * обогащается ECS из ProgressService (counter уникальных юзеров + анонимов
 * по cookie, HybridCache 5 min). Issue #234.
 *
 * При <code>count === 0</code> бейдж не рендерится — пустой счётчик визуально
 * шумит на карточках без интереса.
 */
export interface ViewsBadgeProps {
  count: number;
  className?: string;
  /** Если true — рендерится даже при count=0 (для detail-страницы). */
  showZero?: boolean;
  variant?: "inline" | "pill";
}

export function ViewsBadge({
  count,
  className,
  showZero = false,
  variant = "inline",
}: ViewsBadgeProps) {
  if (count <= 0 && !showZero) {
    return null;
  }

  const formatted = formatViewsCount(count);
  const accessible = `${count.toLocaleString("ru-RU")} ${pluralizeViews(count)}`;

  if (variant === "pill") {
    return (
      <span
        className={cn(
          "inline-flex items-center gap-1 rounded-md bg-muted/70 px-1.5 py-0.5 text-[11px] font-medium text-muted-foreground",
          className,
        )}
        title={accessible}
        aria-label={accessible}
      >
        <Icons.view className="size-3" />
        {formatted}
      </span>
    );
  }

  return (
    <span
      className={cn("inline-flex items-center gap-1 text-muted-foreground", className)}
      title={accessible}
      aria-label={accessible}
    >
      <Icons.view className="size-3" />
      <span className="tabular-nums">{formatted}</span>
    </span>
  );
}

/**
 * 1234 → "1,2K", 12345 → "12K", 1_234_567 → "1,2M". Русская десятичная запятая,
 * без хвостовых нулей. Используется и в badge, и в потенциальных аналитиках.
 */
export function formatViewsCount(n: number): string {
  if (n < 1_000) return n.toString();
  if (n < 1_000_000) {
    // 1-2 значащих цифры в килах: 1,2K / 12K / 999K
    return n < 10_000
      ? `${(n / 1_000).toFixed(1).replace(".", ",").replace(",0", "")}K`
      : `${Math.floor(n / 1_000)}K`;
  }
  return n < 10_000_000
    ? `${(n / 1_000_000).toFixed(1).replace(".", ",").replace(",0", "")}M`
    : `${Math.floor(n / 1_000_000)}M`;
}

function pluralizeViews(n: number): string {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return "просмотр";
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "просмотра";
  return "просмотров";
}
