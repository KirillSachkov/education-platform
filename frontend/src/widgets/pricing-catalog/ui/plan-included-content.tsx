"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { useInfiniteQuery } from "@tanstack/react-query";
import { catalogQueryOptions, type CourseCatalogDto } from "@/entities/course";
import { getCourseKindBadge } from "@/shared/config/course-kind";
import type { PlanCard } from "@/shared/config/landing-plans";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { ContentImage } from "@/shared/ui/components";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";

const PREVIEW_LIMIT = 24;
const FULL_VISIBLE = 6;
const WIDE_VISIBLE = 10;

/**
 * Блок «Что входит» в карточке плана на странице «Доступ» (issue #384).
 *
 * - **COURSE-план** → крупная продуктовая карточка привязанного курса.
 * - **Полный доступ** (`FULL_ALL`/`LEARN_ALL`) → видимая сразу витрина курсов
 *   каталога с aspect-video обложками (это и есть продажа), с «Показать ещё»
 *   если их много.
 *
 * Обложки — из существующего catalog-эндпоинта. react-query дедупит запросы
 * между карточками.
 */
export function PlanIncludedContent({
  plan,
  fullAccessOnly = false,
  variant = "compact",
}: {
  plan: PlanCard;
  /**
   * Только для full-access showcase (#418): отфильтровать выборку каталога по
   * `showInFullAccess === true`. Курсы со снятым флагом (интенсивы, дублирующиеся
   * внутри других курсов) в витрину не попадают. На COURSE-карточку не влияет.
   */
  fullAccessOnly?: boolean;
  /**
   * `compact` — тесная колонка карточки плана (2–3 кол.). `wide` — full-width
   * полоса под основным контентом FullAccessHero (#418): больше колонок +
   * gold-акцент, под стать hero.
   */
  variant?: "compact" | "wide";
}) {
  const isFull = plan.tier === "FULL_ALL" || plan.tier === "LEARN_ALL";
  const isCourse = plan.tier === "COURSE";

  const { data: catalog } = useInfiniteQuery({
    ...catalogQueryOptions({ limit: PREVIEW_LIMIT }),
    enabled: isFull || isCourse,
  });
  const allCourses = catalog?.items ?? [];

  const [showAll, setShowAll] = useState(false);

  // COURSE-план — крупная карточка привязанного курса (фильтр showInFullAccess
  // тут не применяем: показываем именно тот курс, что продаём).
  if (isCourse) {
    const course = allCourses.find((c) => c.id === plan.courseId);
    if (!course) return null;
    return (
      <div className="mt-7">
        <Label>Этот курс входит в план</Label>
        <CourseCard course={course} />
      </div>
    );
  }

  if (!isFull) return null;

  // Full-access showcase скрывает курсы со снятым `showInFullAccess` (#418).
  const courses = fullAccessOnly
    ? allCourses.filter((c) => c.showInFullAccess)
    : allCourses;
  if (courses.length === 0) return null;

  // Разбиваем по типу (#640): «Курсы» отдельно от коротких форматов, чтобы витрина не
  // называла интенсивы/марафоны «курсами». Бейдж каждой карточки — её собственный course.kind.
  const regularCourses = courses.filter((c) => c.kind === "COURSE");
  const shortFormats = courses.filter((c) => c.kind === "INTENSIVE" || c.kind === "MARATHON");

  const isWide = variant === "wide";
  const visibleLimit = isWide ? WIDE_VISIBLE : FULL_VISIBLE;
  const visibleRegular = showAll ? regularCourses : regularCourses.slice(0, visibleLimit);
  const hiddenCount = regularCourses.length - visibleRegular.length;

  const gridClass = cn(
    "grid gap-2.5",
    isWide
      ? "grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5"
      : "grid-cols-2 sm:grid-cols-3",
  );

  return (
    <div
      className={cn(
        isWide
          ? "relative border-t border-border/60 px-7 pb-7 pt-6 sm:px-9 sm:pb-9 lg:px-11"
          : "mt-7",
      )}
    >
      {regularCourses.length > 0 && (
        <>
          <Label gold={isWide}>
            {isWide ? "Курсы в полном доступе" : "Что входит"}
            <span className="ml-1.5 font-normal normal-case tracking-normal text-muted-foreground/80">
              {formatRuPlural(regularCourses.length, RU_PLURALS.course)}
            </span>
          </Label>
          <div className={gridClass}>
            {visibleRegular.map((c) => (
              <CourseCard key={c.id} course={c} />
            ))}
          </div>
          {hiddenCount > 0 && !showAll && (
            <button
              type="button"
              onClick={() => setShowAll(true)}
              className="mt-2.5 w-full rounded-lg border border-border/60 py-2 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted/40 hover:text-foreground"
            >
              Показать ещё {formatRuPlural(hiddenCount, RU_PLURALS.course)}
            </button>
          )}
        </>
      )}
      {shortFormats.length > 0 && (
        <div className={cn(regularCourses.length > 0 && "mt-6")}>
          <Label gold={isWide}>
            Интенсивы и марафоны
            <span className="ml-1.5 font-normal normal-case tracking-normal text-muted-foreground/80">
              {shortFormats.length}
            </span>
          </Label>
          <div className={gridClass}>
            {shortFormats.map((c) => (
              <CourseCard key={c.id} course={c} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function Label({ children, gold = false }: { children: ReactNode; gold?: boolean }) {
  return (
    <div
      className={cn(
        "mb-3 text-[11px] font-semibold uppercase tracking-wider",
        gold ? "text-amber-600 dark:text-amber-300" : "text-muted-foreground",
      )}
    >
      {children}
    </div>
  );
}

function MediaCard({
  href,
  imageUrl,
  title,
  badge,
}: {
  href: string;
  imageUrl: string | null;
  title: string;
  badge?: { label: string; className: string } | null;
}) {
  return (
    <Link
      href={href}
      className="group block overflow-hidden rounded-xl border border-border/50 bg-muted/20 transition-all duration-200 hover:-translate-y-0.5 hover:border-primary/40 hover:shadow-lg hover:shadow-primary/[0.06]"
    >
      <div className="relative aspect-video overflow-hidden bg-muted">
        {imageUrl ? (
          <ContentImage
            src={imageUrl}
            alt={title}
            fill
            sizes="(max-width: 640px) 50vw, 220px"
            className="object-cover transition-transform duration-500 group-hover:scale-105"
          />
        ) : (
          <div className="size-full bg-gradient-to-br from-primary/20 via-primary/10 to-secondary" />
        )}
        {badge ? (
          <span
            className={cn(
              "absolute left-2 top-2 rounded-md px-1.5 py-0.5 text-[10px] font-semibold",
              badge.className,
            )}
          >
            {badge.label}
          </span>
        ) : null}
      </div>
      <div className="p-2.5">
        <div className="truncate text-xs font-semibold text-foreground transition-colors group-hover:text-primary sm:text-[13px]">
          {title}
        </div>
      </div>
    </Link>
  );
}

function CourseCard({ course }: { course: CourseCatalogDto }) {
  const kind = getCourseKindBadge(course.kind);
  return (
    <MediaCard
      href={routes.courseOverview(course.slug)}
      imageUrl={course.imageUrl}
      title={course.title}
      badge={kind ? { label: kind.label, className: kind.class } : null}
    />
  );
}
