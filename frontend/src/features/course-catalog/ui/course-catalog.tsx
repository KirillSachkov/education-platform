"use client";

import Link from "next/link";
import { BookOpen, Loader2 } from "lucide-react";
import { useState } from "react";
import { routes } from "@/shared/config/routes";
import { CourseCatalogCard, catalogQueryOptions } from "@/entities/course";
import { enrollmentQueryOptions } from "@/entities/enrollment";
import { publicPlansQueryOptions } from "@/entities/access-plan";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useInfiniteScroll } from "@/shared/hooks/use-infinite-scroll";
import type { CourseKind } from "@/shared/config/course-kind";
import { cn } from "@/shared/lib/css";
import { CourseKindFilter } from "@/shared/ui/components/course-kind-filter";
import { useSession } from "next-auth/react";
import { useTrackGrowthView } from "@/shared/analytics";
import { filterCoursesByPlanFormat } from "../lib/catalog-plan-format";
import { catalogCountLabel } from "../lib/catalog-count-label";
import { CatalogPlansView } from "./catalog-plans-view";

type CatalogView = "plans" | "all";

export function CourseCatalog() {
  useTrackGrowthView(
    { name: "catalog_view", properties: { catalog_kind: "courses" } },
    "catalog:courses",
  );
  const { data: session } = useSession();
  const isAuthenticated = !!session?.user;

  // «По планам» — дефолт: full-access блок + интенсивы/марафоны. «Всё» — плоский
  // enrollment-aware рендер (как было). Состояние локальное, без URL-синка.
  const [view, setView] = useState<CatalogView>("plans");
  const [kind, setKind] = useState<CourseKind | "all">("all");

  // Публичные планы платформы — один батч на весь каталог (anonymous-ok, дедуп с /pricing).
  // Нужны только для «По планам» вида; план больше не привязан к автору.
  const { data: plans = [], isLoading: isPlansLoading } = useQuery({
    ...publicPlansQueryOptions(),
    enabled: view === "plans",
  });

  const catalogKind = view === "all" && kind !== "all" ? kind : undefined;
  const catalogLimit = view === "plans" ? 100 : 12;
  const { data, isLoading, hasNextPage, isFetchingNextPage, fetchNextPage } = useInfiniteQuery(
    catalogQueryOptions({
      limit: catalogLimit,
      kind: catalogKind,
    }),
  );

  // Bulk-fetch user's enrollments so each card can show a "записан" badge + progress
  // AND drive the «Уже доступно» partition below. limit=100 — потолок бэкенд-валидатора
  // GetMyCourseProgressQuery (>100 → 400 Bad Request); покрывает реальные per-user
  // enrollment-счётчики автора в одной странице. За этим порогом owned-курс может
  // уехать в «Доступно для записи» (#364).
  const { data: enrollmentsData } = useInfiniteQuery({
    ...enrollmentQueryOptions.getMyCourseProgressInfiniteOptions({ limit: 100 }),
    enabled: isAuthenticated,
  });
  const enrollmentByCourseId = new Map(
    (enrollmentsData?.items ?? []).map((item) => [
      item.courseId,
      { progressPercent: item.progressPercent },
    ]),
  );

  const setCursorRef = useInfiniteScroll({
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  });

  const courses = data?.items ?? [];
  const planFilteredCourses =
    view === "plans" ? filterCoursesByPlanFormat(courses, plans, kind) : courses;
  const renderedCourses = view === "plans" ? planFilteredCourses : courses;
  const totalCount =
    view === "plans" && kind !== "all" ? planFilteredCourses.length : (data?.totalCount ?? 0);
  const isCatalogLoading = isLoading || (view === "plans" && isPlansLoading);

  // «Уже доступно» — курсы/интенсивы, к которым у залогиненного юзера уже есть доступ
  // (через enrollment / план). Выносим их в отдельную секцию, чтобы full-access юзеру
  // не маячил каталог того, что он уже имеет (особенно интенсивы). Issue #364.
  // Анонимам нечего «уже иметь» → секция не показывается, каталог как раньше.
  const ownedCourses = isAuthenticated
    ? courses.filter((course) => enrollmentByCourseId.has(course.id) || course.isAccessible)
    : [];
  const availableCourses = isAuthenticated
    ? courses.filter((course) => !enrollmentByCourseId.has(course.id) && !course.isAccessible)
    : courses;

  return (
    <div className="p-3 sm:p-6">
      <div className="mb-4 sm:mb-8">
        <h1 className="text-2xl font-extrabold tracking-tight">Курсы C#, .NET и ASP.NET Core</h1>
        <p className="text-sm text-muted-foreground mt-1 max-w-2xl">
          Обучение C#, .NET и ASP.NET Core с AI-ревью PR — один путь по .NET от Фундамента до
          Software Engineer. Полный доступ открывает направление .NET Fullstack; курсы можно брать и
          по отдельности.
        </p>
        <p className="text-sm text-muted-foreground mt-2">
          Направления:{" "}
          <Link
            href={routes.seoCsharp}
            className="text-foreground underline-offset-2 hover:underline"
          >
            курс C#
          </Link>
          {" · "}
          <Link
            href={routes.seoAspNetCore}
            className="text-foreground underline-offset-2 hover:underline"
          >
            ASP.NET Core
          </Link>
          {" · "}
          <Link
            href={routes.seoDotnet}
            className="text-foreground underline-offset-2 hover:underline"
          >
            .NET обучение
          </Link>
        </p>
        {!isCatalogLoading && (
          <p className="text-sm text-muted-foreground mt-1">
            {catalogCountLabel(totalCount, kind)}
          </p>
        )}
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-3 sm:mb-6">
        <CatalogViewToggle value={view} onChange={setView} />
        <div className="overflow-x-auto">
          <CourseKindFilter value={kind} onChange={setKind} />
        </div>
      </div>

      {isCatalogLoading && (
        <div className="flex items-center justify-center py-24">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!isCatalogLoading && renderedCourses.length > 0 && view === "plans" && (
        <div className="animate-in fade-in duration-300">
          <CatalogPlansView
            courses={planFilteredCourses}
            plans={plans}
            enrollmentByCourseId={enrollmentByCourseId}
          />
        </div>
      )}

      {!isCatalogLoading && renderedCourses.length > 0 && view === "all" && (
        <div className="animate-in fade-in duration-300 space-y-8">
          {ownedCourses.length > 0 && (
            <section className="space-y-4">
              <div>
                <h2 className="text-lg font-semibold tracking-tight">Уже доступно</h2>
                <p className="text-sm text-muted-foreground">
                  Курсы и интенсивы, к которым у вас уже есть доступ
                </p>
              </div>
              <div className="grid grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3 sm:gap-5">
                {ownedCourses.map((course) => (
                  <div key={course.id} className="h-full">
                    <CourseCatalogCard
                      course={course}
                      enrollment={enrollmentByCourseId.get(course.id)}
                    />
                  </div>
                ))}
              </div>
            </section>
          )}

          {availableCourses.length > 0 && (
            <section className="space-y-4">
              {ownedCourses.length > 0 && (
                <h2 className="text-lg font-semibold tracking-tight">Доступно для записи</h2>
              )}
              <div className="grid grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3 sm:gap-5">
                {availableCourses.map((course, index) => (
                  <div key={course.id} className="h-full">
                    <CourseCatalogCard
                      course={course}
                      enrollment={enrollmentByCourseId.get(course.id)}
                      // Catalog grid: xl:grid-cols-4 — top row holds 4 cards on wide screens.
                      priority={index < 4}
                    />
                  </div>
                ))}
              </div>
            </section>
          )}
        </div>
      )}

      {!isCatalogLoading && renderedCourses.length === 0 && (
        <div className="text-center py-24">
          <div className="inline-flex size-16 items-center justify-center rounded-2xl bg-muted mb-4">
            <BookOpen size={28} className="text-muted-foreground" />
          </div>
          <p className="text-sm font-medium text-foreground">
            {kind === "all" ? "Пока нет доступных курсов" : "По выбранному типу ничего не нашлось"}
          </p>
        </div>
      )}

      <div ref={setCursorRef} className="h-1" />

      {isFetchingNextPage && (
        <div className="flex justify-center py-4">
          <Loader2 className="size-5 animate-spin text-muted-foreground" />
        </div>
      )}
    </div>
  );
}

const VIEW_OPTIONS: { value: CatalogView; label: string }[] = [
  { value: "plans", label: "По планам" },
  { value: "all", label: "Всё" },
];

/**
 * Segmented-control «По планам / Всё» — тот же паттерн, что `CourseKindFilter`:
 * `role="group"` + нативные `<button>` с `aria-pressed`, touch-таргеты ≥44px.
 */
function CatalogViewToggle({
  value,
  onChange,
}: {
  value: CatalogView;
  onChange: (value: CatalogView) => void;
}) {
  return (
    <div
      role="group"
      aria-label="Вид каталога"
      className="inline-flex items-center gap-1 rounded-lg border bg-muted/50 p-1"
    >
      {VIEW_OPTIONS.map((option) => {
        const isActive = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={isActive}
            onClick={() => onChange(option.value)}
            className={cn(
              "min-h-[44px] rounded-md px-3 text-sm font-medium whitespace-nowrap transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
              isActive
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
