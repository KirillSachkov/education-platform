"use client";

import Link from "next/link";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";

interface CourseAttributionBadgeProps {
  /** Название курса. Если null — показываем «Без курса». */
  courseTitle: string | null | undefined;
  /** Ссылка на страницу курса. Если задана — бейдж становится кликабельным. */
  href?: string | null;
  /**
   * `inline` — стандартный бейдж в ряд с другими метаполями.
   * `overlay` — позиционируется абсолютно (top-left обложки карточки).
   */
  variant?: "inline" | "overlay";
  className?: string;
}

/**
 * Маркер принадлежности контента курсу. Показывается на карточках материалов,
 * подборок, закладок — чтобы в ленте / глобальном поиске / выдаче было понятно,
 * откуда этот контент.
 *
 * Если `courseTitle` нет — рендерит нейтральную плашку «Без курса», так что
 * orphan-материалы не выглядят как обычные.
 */
export function CourseAttributionBadge({
  courseTitle,
  href,
  variant = "inline",
  className,
}: CourseAttributionBadgeProps) {
  const isOrphan = !courseTitle;
  const label = courseTitle ?? "Без курса";

  const base = cn(
    "inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-[10px] font-medium max-w-full",
    isOrphan
      ? "border-border/50 bg-muted/40 text-muted-foreground"
      : "border-primary/20 bg-primary/5 text-primary hover:bg-primary/10",
    variant === "overlay"
      ? "absolute left-2 top-2 backdrop-blur-sm bg-black/45 border-white/15 text-white/90 hover:bg-black/60"
      : "",
    className,
  );

  const inner = (
    <>
      <Icons.course className="size-3 shrink-0" />
      <span className="truncate">{label}</span>
    </>
  );

  if (href && !isOrphan) {
    return (
      <Link
        href={href}
        prefetch={false}
        className={cn(base, "transition-colors")}
        title={label}
        onClick={(e) => e.stopPropagation()}
      >
        {inner}
      </Link>
    );
  }

  return (
    <span className={base} title={label}>
      {inner}
    </span>
  );
}
