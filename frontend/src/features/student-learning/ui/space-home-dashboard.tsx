"use client";

import { type ReactNode, useState } from "react";
import Link from "next/link";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { catalogQueryOptions, CourseCatalogCard } from "@/entities/course";
import { lastActiveCourseQueryOptions } from "@/entities/enrollment";
import { trackGrowthEvent } from "@/shared/analytics";
import { formatRelativeDate } from "@/shared/lib/date";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { cn } from "@/shared/lib/css";
import { routes } from "@/shared/config/routes";
import { type CourseKind, getCourseKindBadge } from "@/shared/config/course-kind";
import { CourseKindFilter } from "@/shared/ui/components/course-kind-filter";
import { AnimatedNumber } from "@/shared/ui/components/animated-number";
import { ContentImage } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useMyXpProgress } from "@/entities/user-progress";
import { useLeaderboard } from "@/entities/leaderboard";
import { courseProgressCardKey } from "../lib/course-progress-card-key";
import { groupLearningCoursesByKind } from "../model/learning-course-groups";
import { useSpaceMyCourseProgress } from "../model/use-space-my-course-progress";
import { RecentDiscussions } from "./recent-discussions";
import type { LastActiveCourseDto, UserCourseProgressDto } from "@/entities/enrollment";

const COURSE_INITIAL_VISIBLE = 10;
const COURSE_EXPAND_STEP = 4;

export function AuthenticatedHome({
  pinsSlot,
  statsSlot,
}: {
  /**
   * Slot для секции закреплённых материалов (`features/home-pins`). Композируется
   * на уровне страницы (`home-client.tsx`), чтобы `features/student-learning` не
   * импортировал `features/home-pins` напрямую (FSD: no cross-slice imports).
   * Рендерится сразу под прогрессом (`LevelStatsCard`) и над курсами — это
   * «с чего начать», поэтому стоит высоко (см. frontend/CLAUDE.md home composition).
   */
  pinsSlot?: ReactNode;
  /**
   * Slot для блока персональной статистики (`widgets/home-stats`, #684) — стрик,
   * heatmap активности, KPI, промо тренажёра. Тоже композируется на уровне
   * страницы (FSD: feature не импортирует widget). Рендерится под `LevelStatsCard`,
   * продолжая «вашу статистику» прогресса.
   */
  statsSlot?: ReactNode;
}) {
  const {
    items: courses,
    totalCount,
    hasNextPage,
    fetchNextPage,
    isLoading,
    isFetchingNextPage,
    error,
    refetch,
  } = useSpaceMyCourseProgress(12);

  const showCoursesSection = !isLoading && courses.length > 0;

  const completedCourses = courses.filter((c) => c.progressPercent === 100).length;

  // Совсем новый юзер без записей — вместо пустого «Запишитесь на курс» показываем
  // каталог, чтобы сразу было откуда начать. lastActiveCourse дедупится с
  // ContinueLearningCard (тот же queryKey), лишнего запроса нет.
  const { data: lastActiveCourse, isLoading: isLastCourseLoading } = useQuery(
    lastActiveCourseQueryOptions(),
  );
  const hasNoCourses =
    !isLoading && !isLastCourseLoading && courses.length === 0 && !lastActiveCourse;

  return (
    <div className="space-y-6 sm:space-y-8">
      {!hasNoCourses && <ContinueLearningCard />}

      <LevelStatsCard totalCourses={totalCount} completedCourses={completedCourses} />

      {statsSlot}

      {pinsSlot}

      {showCoursesSection ? (
        <MyCoursesSection
          courses={courses}
          totalCount={totalCount}
          hasNextPage={hasNextPage}
          fetchNextPage={() => void fetchNextPage()}
          isFetchingNextPage={isFetchingNextPage}
        />
      ) : isLoading ? (
        <Skeleton className="h-24 rounded-xl" />
      ) : error ? (
        <ErrorCard error={error} onRetry={() => void refetch()} />
      ) : hasNoCourses ? (
        <HomeCatalogSection />
      ) : null}

      <RecentDiscussions />
    </div>
  );
}

/**
 * Каталог на главной для юзера без записанных курсов — заменяет пустое
 * «Запишитесь на курс». Топ-8 курсов + ссылка на полный каталог.
 */
function HomeCatalogSection() {
  const { data, isLoading } = useInfiniteQuery(catalogQueryOptions({ limit: 8 }));
  const courses = (data?.items ?? []).slice(0, 8);

  return (
    <section className="space-y-2 sm:space-y-3">
      <SectionHeader
        title="Каталог курсов"
        description="Выберите курс, чтобы начать обучение"
        actionHref={routes.courses}
        actionLabel="Весь каталог"
      />
      {isLoading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 sm:gap-5">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-64 rounded-xl" />
          ))}
        </div>
      ) : courses.length === 0 ? (
        <p className="rounded-md border border-dashed bg-muted/30 px-4 py-8 text-center text-sm text-muted-foreground">
          Пока нет доступных курсов
        </p>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 sm:gap-5">
          {courses.map((course, index) => (
            <CourseCatalogCard key={course.id} course={course} priority={index < 2} />
          ))}
        </div>
      )}
    </section>
  );
}

function MyCoursesSection({
  courses,
  totalCount,
  hasNextPage,
  fetchNextPage,
  isFetchingNextPage,
}: {
  courses: UserCourseProgressDto[];
  totalCount: number;
  hasNextPage: boolean;
  fetchNextPage: () => void;
  isFetchingNextPage: boolean;
}) {
  const [visibleCount, setVisibleCount] = useState(COURSE_INITIAL_VISIBLE);
  const visibleCourses = courses.slice(0, visibleCount);
  const {
    courses: regularCourses,
    intensives,
    marathons,
  } = groupLearningCoursesByKind(visibleCourses);
  const remainingLocal = Math.max(0, courses.length - visibleCount);

  const handleShowMore = () => {
    if (visibleCount < courses.length) {
      setVisibleCount((n) => Math.min(n + COURSE_EXPAND_STEP, courses.length));
    } else if (hasNextPage) {
      fetchNextPage();
    }
  };

  const canShowMore = visibleCount < courses.length || hasNextPage;

  return (
    <section className="space-y-2 sm:space-y-3">
      <SectionHeader title="Моё обучение" meta={formatRuPlural(totalCount, RU_PLURALS.course)} />
      {regularCourses.length > 0 && (
        <div className="space-y-2 sm:space-y-3">
          <h3 className="text-sm font-semibold text-muted-foreground">Курсы</h3>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 sm:gap-5">
            {regularCourses.map((course) => (
              <CourseProgressCard key={courseProgressCardKey(course)} course={course} />
            ))}
          </div>
        </div>
      )}
      {intensives.length > 0 && (
        <div className="space-y-2 sm:space-y-3 pt-1">
          <h3 className="text-sm font-semibold text-muted-foreground">Интенсивы</h3>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 sm:gap-5">
            {intensives.map((course) => (
              <CourseProgressCard key={courseProgressCardKey(course)} course={course} />
            ))}
          </div>
        </div>
      )}
      {marathons.length > 0 && (
        <div className="space-y-2 sm:space-y-3 pt-1">
          <h3 className="text-sm font-semibold text-muted-foreground">Марафоны</h3>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 sm:gap-5">
            {marathons.map((course) => (
              <CourseProgressCard key={courseProgressCardKey(course)} course={course} />
            ))}
          </div>
        </div>
      )}
      {canShowMore && (
        <div className="flex justify-center pt-1">
          <Button
            variant="ghost"
            size="sm"
            onClick={handleShowMore}
            disabled={isFetchingNextPage}
            className="gap-1.5 text-xs"
          >
            {isFetchingNextPage && <Icons.loading className="size-3.5 animate-spin" />}
            Показать ещё
            {!isFetchingNextPage && remainingLocal > 0 && (
              <span className="text-muted-foreground tabular-nums">
                +{Math.min(COURSE_EXPAND_STEP, remainingLocal)}
              </span>
            )}
          </Button>
        </div>
      )}
    </section>
  );
}

function SectionHeader({
  title,
  description,
  actionHref,
  actionLabel,
  meta,
}: {
  title: string;
  description?: string;
  actionHref?: string;
  actionLabel?: string;
  meta?: string;
}) {
  return (
    <div className="flex items-end justify-between gap-3">
      <div className="min-w-0">
        <h2 className="text-base sm:text-lg md:text-xl font-semibold tracking-tight">{title}</h2>
        {description && (
          <p className="text-xs sm:text-sm text-muted-foreground mt-0.5">{description}</p>
        )}
      </div>
      {actionHref && actionLabel ? (
        <Link
          href={actionHref}
          className="text-xs sm:text-sm text-muted-foreground hover:text-primary transition-colors shrink-0 inline-flex items-center gap-1"
        >
          {actionLabel}
          <Icons.arrowRight className="size-3.5" />
        </Link>
      ) : meta ? (
        <span className="text-xs text-muted-foreground tabular-nums shrink-0">{meta}</span>
      ) : null}
    </div>
  );
}

function CourseMetaLine({ course }: { course: { lastActivityAt: string | null } }) {
  const lastActivity = course.lastActivityAt;
  if (!lastActivity) return null;

  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[10px] sm:text-[11px] text-muted-foreground pt-0.5">
      <span>Активность {formatRelativeDate(lastActivity)}</span>
    </div>
  );
}

function CourseProgressCard({ course }: { course: UserCourseProgressDto }) {
  const isCompleted = course.progressPercent === 100;
  return (
    <Link href={routes.courseOverview(course.courseSlug)} prefetch={false} className="block group">
      <Card className="overflow-hidden gap-0 py-0 hover:border-primary/40 hover:shadow-lg hover:shadow-primary/[0.06] hover:-translate-y-0.5 transition-all duration-200 h-full">
        <div className="relative aspect-video overflow-hidden bg-muted">
          {course.imageUrl ? (
            <ContentImage
              src={course.imageUrl}
              alt={course.title}
              fill
              sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 33vw"
              className="object-cover group-hover:scale-[1.03] transition-transform duration-500 ease-out"
            />
          ) : (
            <div className="w-full h-full bg-gradient-to-br from-primary/20 via-primary/10 to-secondary" />
          )}
          <div className="absolute inset-0 bg-gradient-to-t from-background/90 via-background/20 to-transparent" />
          {isCompleted && (
            <div className="absolute inset-0 bg-emerald-500/30 flex items-center justify-center">
              <Icons.completed className="size-10 text-white drop-shadow" />
            </div>
          )}
          {course.kind && (
            <Badge
              className={cn(
                "absolute top-2 right-2 border-0 text-xs",
                getCourseKindBadge(course.kind).class,
              )}
            >
              {getCourseKindBadge(course.kind).label}
            </Badge>
          )}
        </div>
        <CardContent className="p-4 flex flex-col flex-1">
          <h3 className="text-sm font-bold text-foreground mb-3 line-clamp-2 group-hover:text-primary transition-colors">
            {course.title}
          </h3>
          <div className="space-y-1.5 mt-auto">
            <div className="flex items-center justify-between gap-2 text-xs">
              <span
                className={cn("font-semibold", isCompleted ? "text-emerald-500" : "text-primary")}
              >
                {isCompleted ? "Курс пройден" : "Продолжить обучение"}
              </span>
              <span
                className={cn(
                  "tabular-nums",
                  isCompleted ? "text-emerald-500 font-semibold" : "text-muted-foreground",
                )}
              >
                {course.progressPercent}%
              </span>
            </div>
            <div className="h-1 w-full overflow-hidden rounded-full bg-muted">
              <div
                className={cn(
                  "h-full transition-all",
                  isCompleted ? "bg-emerald-500" : "bg-primary",
                )}
                style={{ width: `${course.progressPercent}%` }}
              />
            </div>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[11px] tabular-nums text-muted-foreground">
              {course.totalModules > 0 && (
                <span className="inline-flex items-center gap-1">
                  <Icons.module className="size-3" aria-hidden />
                  {course.completedModules}/{course.totalModules} модулей
                </span>
              )}
              {course.totalMaterials > 0 && (
                <span className="inline-flex items-center gap-1">
                  <Icons.article className="size-3" aria-hidden />
                  {course.completedMaterials}/{course.totalMaterials} материалов
                </span>
              )}
              {course.totalIssues > 0 && (
                <span className="inline-flex items-center gap-1">
                  <Icons.issue className="size-3" aria-hidden />
                  {course.completedIssues}/{course.totalIssues} задач
                </span>
              )}
            </div>
            <CourseMetaLine course={course} />
          </div>
        </CardContent>
      </Card>
    </Link>
  );
}

export function GuestHome() {
  return (
    <div className="space-y-6 sm:space-y-8">
      <GuestCoursesSection />
    </div>
  );
}

function GuestCoursesSection() {
  const INITIAL_COUNT = 6;
  const STEP = 6;
  const [visibleCount, setVisibleCount] = useState(INITIAL_COUNT);
  const [kind, setKind] = useState<CourseKind | "all">("all");
  const { data, isLoading, error, refetch } = useInfiniteQuery(
    catalogQueryOptions({
      limit: 100,
      kind: kind === "all" ? undefined : kind,
    }),
  );
  const courses = data?.items ?? [];

  if (isLoading) {
    return (
      <section className="space-y-2 sm:space-y-3">
        <SectionHeader title="Курсы" description="Все курсы платформы" />
        <div className="grid gap-4 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-64 rounded-xl" />
          ))}
        </div>
      </section>
    );
  }

  if (error) {
    return (
      <section className="space-y-2 sm:space-y-3">
        <SectionHeader title="Курсы" description="Все курсы платформы" />
        <ErrorCard error={error} onRetry={() => void refetch()} />
      </section>
    );
  }

  // Раздел показываем всегда (даже если у автора пока нет курсов) — пустое
  // состояние рендерится ниже. Так home-страница держит единый набор секций.
  const visible = courses.slice(0, visibleCount);
  const hasMore = courses.length > visibleCount;
  const remaining = courses.length - visibleCount;

  return (
    <section className="space-y-2 sm:space-y-3">
      <SectionHeader
        title="Курсы"
        description="Один путь по .NET: Фундамент → Software Engineer. Бесплатные материалы — без оплаты."
      />
      <div className="overflow-x-auto">
        <CourseKindFilter value={kind} onChange={setKind} />
      </div>
      {courses.length === 0 ? (
        <p className="rounded-md border border-dashed bg-muted/30 px-4 py-8 text-center text-sm text-muted-foreground">
          {kind === "all" ? "Курсов пока нет" : "По выбранному типу ничего не нашлось"}
        </p>
      ) : (
        <div className="grid gap-4 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3">
          {visible.map((course, index) => (
            <CourseCatalogCard key={course.id} course={course} priority={index < 2} />
          ))}
        </div>
      )}
      {hasMore && (
        <div className="flex justify-center pt-1">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setVisibleCount((n) => n + STEP)}
            className="gap-1.5"
          >
            Показать ещё
            <span className="text-muted-foreground tabular-nums">
              {remaining < STEP ? formatRuPlural(remaining, RU_PLURALS.course) : `+${STEP}`}
            </span>
            <Icons.chevronRight className="size-3.5 rotate-90" />
          </Button>
        </div>
      )}
    </section>
  );
}

function resolveContinueHref(course: LastActiveCourseDto): string {
  const { lastPosition, courseSlug } = course;
  if (!lastPosition) return routes.courseOverview(courseSlug);
  if (lastPosition.entityType === "MATERIAL") {
    return routes.courseMaterial(courseSlug, lastPosition.entityId);
  }
  return routes.courseIssue(courseSlug, lastPosition.entityId);
}

function ContinueLearningCard() {
  const { data: lastCourse, isLoading } = useQuery(lastActiveCourseQueryOptions());

  if (isLoading) {
    return <Skeleton className="h-28 sm:h-36 md:h-44 rounded-2xl" />;
  }

  if (!lastCourse) {
    return (
      <Card className="border-dashed">
        <CardContent className="p-8 sm:p-10 flex flex-col items-center justify-center gap-3 text-center">
          <div className="size-12 rounded-full bg-primary/10 border border-primary/20 flex items-center justify-center">
            <Icons.course className="size-6 text-primary" />
          </div>
          <p className="text-sm text-muted-foreground">Запишитесь на курс, чтобы начать обучение</p>
        </CardContent>
      </Card>
    );
  }

  const continueHref = resolveContinueHref(lastCourse);
  const progressPercent = lastCourse.progressPercent;
  const isFreshStart = progressPercent === 0;

  return (
    <Card className="group relative overflow-hidden border-border/60 hover:border-primary/40 transition-colors duration-300 py-0 shadow-md shadow-black/10">
      {/* Ambient backdrop (desktop only) */}
      {lastCourse.imageUrl && (
        <div className="hidden sm:block absolute inset-0 pointer-events-none" aria-hidden="true">
          <ContentImage
            src={lastCourse.imageUrl}
            alt=""
            fill
            sizes="100vw"
            className="object-cover scale-110 opacity-25 blur-2xl"
          />
          <div className="absolute inset-0 bg-gradient-to-r from-card via-card/95 to-card/50" />
        </div>
      )}

      <CardContent className="relative p-3 sm:p-4 md:p-5">
        <div className="flex flex-row items-center gap-3 sm:gap-4 md:gap-5">
          {/* Cover */}
          <div className="relative shrink-0 w-24 sm:w-32 md:w-44">
            {lastCourse.imageUrl ? (
              <div className="relative aspect-video w-full rounded-lg sm:rounded-xl overflow-hidden border border-white/10 shadow-md bg-background/40">
                <ContentImage
                  src={lastCourse.imageUrl}
                  alt={lastCourse.title}
                  fill
                  sizes="(max-width: 640px) 96px, (max-width: 768px) 128px, 176px"
                  // Continue-learning card sits above the fold on /home and is
                  // the most likely LCP element. Mark high-priority + eager
                  // fetch so the browser doesn't deprioritize behind the bg
                  // blur image.
                  loading="eager"
                  fetchPriority="high"
                  className="object-cover"
                />
              </div>
            ) : (
              <div className="relative aspect-video w-full rounded-lg sm:rounded-xl border border-primary/20 bg-gradient-to-br from-primary/25 via-primary/10 to-transparent flex items-center justify-center shadow-md">
                <Icons.course className="size-7 sm:size-9 md:size-10 text-primary/70" />
              </div>
            )}
          </div>

          {/* Content */}
          <div className="flex-1 min-w-0 flex flex-col gap-1.5 sm:gap-2">
            <div className="flex items-center gap-1.5 min-w-0">
              <span className="relative flex size-1.5 shrink-0" aria-hidden="true">
                <span className="absolute inline-flex h-full w-full rounded-full bg-primary opacity-60 animate-ping" />
                <span className="relative inline-flex size-1.5 rounded-full bg-primary" />
              </span>
              <span className="text-[10px] sm:text-[11px] font-bold uppercase tracking-[0.16em] text-primary truncate">
                {isFreshStart ? "Начать обучение" : "Продолжить обучение"}
              </span>
            </div>

            <h2 className="text-sm sm:text-base md:text-lg font-bold tracking-tight leading-tight text-foreground line-clamp-2">
              {lastCourse.title}
            </h2>

            <div className="space-y-1">
              <div className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[10px] sm:text-[11px] text-muted-foreground tabular-nums">
                {lastCourse.totalModules > 0 && (
                  <span className="inline-flex items-center gap-1">
                    <Icons.module className="size-3 sm:size-3.5" aria-hidden />
                    {lastCourse.completedModules}/{lastCourse.totalModules} модулей
                  </span>
                )}
                {lastCourse.totalMaterials > 0 && (
                  <span className="inline-flex items-center gap-1">
                    <Icons.article className="size-3 sm:size-3.5" aria-hidden />
                    {lastCourse.completedMaterials}/{lastCourse.totalMaterials} материалов
                  </span>
                )}
                {lastCourse.totalIssues > 0 && (
                  <span className="inline-flex items-center gap-1">
                    <Icons.issue className="size-3 sm:size-3.5" aria-hidden />
                    {lastCourse.completedIssues}/{lastCourse.totalIssues} задач
                  </span>
                )}
                <span className="ml-auto text-foreground font-semibold">{progressPercent}%</span>
              </div>
              <div className="relative h-1 sm:h-1.5 rounded-full bg-secondary/70 overflow-hidden">
                <div
                  className="absolute inset-y-0 left-0 rounded-full bg-gradient-to-r from-primary/70 via-primary to-primary/90 transition-[width] duration-1000 ease-out"
                  style={{ width: `${progressPercent}%` }}
                />
              </div>
              <CourseMetaLine course={lastCourse} />
            </div>

            <Button
              asChild
              size="sm"
              className="group/cta self-start mt-1 h-8 sm:h-9 px-3 text-xs sm:text-sm font-semibold rounded-lg shadow-sm shadow-primary/20 hover:shadow-md hover:shadow-primary/30 transition-shadow"
            >
              <Link
                href={continueHref}
                onClick={() =>
                  trackGrowthEvent({
                    name: "continue_learning_click",
                    properties: {
                      target_type:
                        lastCourse.lastPosition?.entityType === "MATERIAL"
                          ? "material"
                          : lastCourse.lastPosition?.entityType === "ISSUE"
                            ? "issue"
                            : "course",
                      course_id: lastCourse.courseId,
                      content_id: lastCourse.lastPosition?.entityId,
                    },
                  })
                }
              >
                <Icons.playCircle className="size-3.5 sm:size-4" />
                <span className="sm:hidden">{isFreshStart ? "Начать" : "Продолжить"}</span>
                <span className="hidden sm:inline">
                  {isFreshStart ? "Начать обучение" : "Продолжить обучение"}
                </span>
                <Icons.arrowRight className="size-3.5 sm:size-4 group-hover/cta:translate-x-0.5 transition-transform" />
              </Link>
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function LevelStatsCard({
  totalCourses,
  completedCourses,
}: {
  totalCourses: number;
  completedCourses: number;
}) {
  const xp = useMyXpProgress();
  // 1-item page is the cheapest way to populate `currentUser` (the caller's
  // rank) via the existing leaderboard contract — the row itself is ignored.
  const { data: leaderboard } = useLeaderboard({ pageSize: 1 });

  if (xp.isLoading) {
    return <Skeleton className="h-16 sm:h-20 rounded-xl" />;
  }
  if (xp.error) return null;

  const xpToNext = xp.xpToNextLevel;
  const rank = leaderboard?.currentUser?.rank;

  return (
    <Card className="relative overflow-hidden border-border/60 py-0">
      <CardContent className="relative p-3 sm:p-4">
        <div className="flex items-center gap-3 sm:gap-4">
          {/* Level orb */}
          <div className="relative shrink-0">
            <div className="size-12 sm:size-14 rounded-full bg-gradient-to-br from-gold/30 via-gold/15 to-transparent border border-gold/30 flex items-center justify-center text-gold font-bold text-base sm:text-lg shadow-md shadow-gold/10">
              <AnimatedNumber value={xp.currentLevel} />
            </div>
          </div>

          {/* XP block */}
          <div className="min-w-0 flex-1 flex flex-col gap-1">
            <div className="flex items-baseline justify-between gap-2 min-w-0">
              <span className="text-[10px] sm:text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Уровень {xp.currentLevel}
              </span>
              <span className="text-[10px] sm:text-xs text-muted-foreground tabular-nums shrink-0">
                <AnimatedNumber value={xp.totalXp} />
                {xpToNext != null && (
                  <>
                    <span className="text-border mx-0.5">/</span>
                    {xp.nextLevelXpThreshold.toLocaleString()}
                  </>
                )}
                <span className="text-gold font-semibold ml-1">XP</span>
              </span>
            </div>
            <div className="relative h-1 sm:h-1.5 rounded-full bg-secondary/70 overflow-hidden">
              <div
                className="absolute inset-y-0 left-0 rounded-full bg-gradient-to-r from-gold/70 via-gold to-gold/90 transition-[width] duration-1000"
                style={{ width: `${Math.min(100, xp.progressPercent)}%` }}
              />
            </div>
            {xpToNext != null && xpToNext > 0 && (
              <span className="text-[10px] sm:text-[11px] text-muted-foreground/80 tabular-nums">
                До {xp.nextLevel ?? xp.currentLevel + 1} уровня — {xpToNext.toLocaleString()} XP
              </span>
            )}
          </div>

          {/* Right stats — rank (all sizes, links to leaderboard) + courses (desktop) */}
          <div className="flex items-center gap-3 sm:gap-4 shrink-0 pl-3 sm:pl-4 border-l border-border/40">
            {rank != null && (
              <Link
                href={routes.platformLeaderboard}
                prefetch={false}
                className="flex flex-col items-end gap-0.5 transition-opacity hover:opacity-80"
              >
                <span className="text-[10px] sm:text-xs text-muted-foreground">Рейтинг</span>
                <span className="inline-flex items-center gap-1 text-sm font-semibold tabular-nums">
                  <Icons.trophy className="size-3.5 text-gold" aria-hidden />#
                  <AnimatedNumber value={rank} />
                </span>
              </Link>
            )}
            <div className="hidden sm:flex flex-col items-end gap-0.5 text-right">
              <span className="text-xs text-muted-foreground">Курсов</span>
              <span className="text-sm font-semibold tabular-nums">
                {completedCourses}
                <span className="text-muted-foreground/60 mx-0.5">/</span>
                {totalCourses}
              </span>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
