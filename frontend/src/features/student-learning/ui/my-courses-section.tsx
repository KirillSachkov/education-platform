"use client";

import { useState } from "react";
import { useMyCourseProgress } from "../model/use-my-course-progress";
import { courseProgressCardKey } from "../lib/course-progress-card-key";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Icons } from "@/shared/ui/icons";
import type { ReactNode } from "react";
import type { UserCourseProgressDto } from "@/entities/enrollment";
import type { CourseKind } from "@/shared/config/course-kind";
import { CourseKindFilter } from "@/shared/ui/components/course-kind-filter";
import { MyCourseProgressCard } from "./my-course-progress-card";
import { MyCoursesEmptyState } from "./my-courses-empty-state";
import { MyCoursesSkeleton } from "./my-courses-skeleton";

const COURSES_PAGE_LIMIT = 6;
const COURSES_PAGE_LIMIT_WITH_CONTROLS = 50;

type SortKey = "recent" | "progress-desc" | "progress-asc" | "title";
type StatusFilter = "all" | "active" | "completed";

interface MyCoursesSectionProps {
  /** Hard cap on the items shown before any pagination kicks in. Defaults to 6. */
  limit?: number;
  /** Override the section heading. `null` hides the header entirely (use when caller wraps in its own h2). */
  title?: string | null;
  /** Override the section subheading. `null` hides it. */
  subtitle?: string | null;
  /**
   * Whether to render the «Показать ещё» button when more pages exist. Set to
   * `false` on dashboards where the section is a teaser (link out to a focused
   * page instead).
   */
  showLoadMore?: boolean;
  /**
   * Whether to render search, sort and status-filter controls above the grid.
   * Off by default — only the /courses focused page enables this.
   */
  showControls?: boolean;
  /** Slot rendered after the cards grid — typically a «Все курсы →» link. */
  footerSlot?: ReactNode;
}

export function MyCoursesSection({
  limit,
  title = "Продолжить обучение",
  subtitle = "Ваши курсы и текущий прогресс по каждому из них",
  showLoadMore = true,
  showControls = false,
  footerSlot,
}: MyCoursesSectionProps = {}) {
  const effectiveLimit =
    limit ?? (showControls ? COURSES_PAGE_LIMIT_WITH_CONTROLS : COURSES_PAGE_LIMIT);

  const [searchQuery, setSearchQuery] = useState("");
  const [sortKey, setSortKey] = useState<SortKey>("recent");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [kind, setKind] = useState<CourseKind | "all">("all");

  const {
    items,
    hasNextPage,
    fetchNextPage,
    isLoading,
    isFetchingNextPage,
    error,
    refetch,
  } = useMyCourseProgress(effectiveLimit);

  // ProgressService «my progress» feed has no `kind` query param → filter client-side.
  // `c.kind` may be undefined while the backend hasn't shipped it; such items show only
  // under «Все» and never match a specific kind.
  const kindFilteredItems =
    kind === "all" ? items : items.filter((c) => c.kind === kind);
  const filteredItems = showControls
    ? applyControls(kindFilteredItems, searchQuery, sortKey, statusFilter)
    : kindFilteredItems;

  return (
    <section className="space-y-4">
      {(title || subtitle) && (
        <div className="flex items-end justify-between gap-3">
          <div>
            {title && <h2 className="text-lg font-semibold">{title}</h2>}
            {subtitle && <p className="text-sm text-muted-foreground">{subtitle}</p>}
          </div>
        </div>
      )}

      {!isLoading && items.length > 0 && (
        <div className="overflow-x-auto">
          <CourseKindFilter value={kind} onChange={setKind} />
        </div>
      )}

      {showControls && !isLoading && items.length > 0 && (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="relative w-full sm:max-w-xs">
            <Icons.search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              type="search"
              placeholder="Поиск по названию"
              aria-label="Поиск по моим курсам"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="pl-9"
            />
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusFilterChips value={statusFilter} onChange={setStatusFilter} />
            <Select value={sortKey} onValueChange={(v) => setSortKey(v as SortKey)}>
              <SelectTrigger aria-label="Сортировка" className="w-full sm:w-44">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="recent">Недавняя активность</SelectItem>
                <SelectItem value="progress-desc">Прогресс ↓</SelectItem>
                <SelectItem value="progress-asc">Прогресс ↑</SelectItem>
                <SelectItem value="title">По названию</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
      )}

      {isLoading ? (
        <MyCoursesSkeleton />
      ) : error ? (
        <ErrorCard error={error} onRetry={() => void refetch()} />
      ) : items.length === 0 ? (
        <MyCoursesEmptyState />
      ) : filteredItems.length === 0 ? (
        <p className="rounded-md border border-dashed bg-muted/30 px-4 py-8 text-center text-sm text-muted-foreground">
          По заданным фильтрам ничего не нашлось
        </p>
      ) : (
        <>
          <div className="grid gap-3 sm:gap-4 grid-cols-2 lg:grid-cols-3">
            {filteredItems.map((course, index) => (
              <MyCourseProgressCard
                key={courseProgressCardKey(course)}
                course={course}
                priority={index < 2}
              />
            ))}
          </div>

          {showLoadMore && hasNextPage && (
            <div className="flex justify-center">
              <Button
                variant="outline"
                onClick={() => void fetchNextPage()}
                disabled={isFetchingNextPage}
              >
                {isFetchingNextPage && <Icons.loading className="size-4 animate-spin" />}
                Показать ещё
              </Button>
            </div>
          )}

          {footerSlot && <div className="pt-2">{footerSlot}</div>}
        </>
      )}
    </section>
  );
}

function StatusFilterChips({
  value,
  onChange,
}: {
  value: StatusFilter;
  onChange: (v: StatusFilter) => void;
}) {
  const chips: { key: StatusFilter; label: string }[] = [
    { key: "all", label: "Все" },
    { key: "active", label: "В процессе" },
    { key: "completed", label: "Завершённые" },
  ];
  return (
    <div
      role="group"
      aria-label="Фильтр по статусу"
      className="inline-flex rounded-md border bg-muted/30 p-0.5"
    >
      {chips.map((chip) => {
        const active = value === chip.key;
        return (
          <button
            key={chip.key}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(chip.key)}
            className={
              "rounded-sm px-3 py-1 text-xs font-medium transition-colors min-h-[28px] " +
              (active
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground")
            }
          >
            {chip.label}
          </button>
        );
      })}
    </div>
  );
}

function applyControls(
  items: UserCourseProgressDto[],
  search: string,
  sort: SortKey,
  status: StatusFilter,
): UserCourseProgressDto[] {
  const query = search.trim().toLocaleLowerCase("ru");
  const matched = items.filter((item) => {
    if (status === "active" && item.progressPercent >= 100) return false;
    if (status === "completed" && item.progressPercent < 100) return false;
    if (!query) return true;
    return item.title.toLocaleLowerCase("ru").includes(query);
  });

  switch (sort) {
    case "progress-desc":
      return [...matched].sort((a, b) => b.progressPercent - a.progressPercent);
    case "progress-asc":
      return [...matched].sort((a, b) => a.progressPercent - b.progressPercent);
    case "title":
      return [...matched].sort((a, b) => a.title.localeCompare(b.title, "ru"));
    case "recent":
    default:
      return [...matched].sort((a, b) => {
        const aKey = a.lastActivityAt ?? a.enrolledAt;
        const bKey = b.lastActivityAt ?? b.enrolledAt;
        return bKey.localeCompare(aKey);
      });
  }
}
