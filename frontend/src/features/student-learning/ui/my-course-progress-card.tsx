"use client";

import type { UserCourseProgressDto } from "@/entities/enrollment";
import { formatRelativeDate } from "@/shared/lib/date";
import { pluralize } from "@/shared/lib/pluralize";
import { getCourseKindBadge } from "@/shared/config/course-kind";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { ProgressBar } from "@/shared/ui/components/progress-bar";
import { ContentImage } from "@/shared/ui/components";
import Link from "next/link";

interface MyCourseProgressCardProps {
  course: UserCourseProgressDto;
  /** Set true for the first 1-2 cards above the fold to hint the browser to fetch the cover eagerly. */
  priority?: boolean;
}

export function MyCourseProgressCard({ course, priority = false }: MyCourseProgressCardProps) {
  const kindBadge = course.kind ? getCourseKindBadge(course.kind) : null;
  return (
    <Card className="overflow-hidden gap-0 py-0 group hover:border-primary/40 hover:shadow-lg hover:shadow-primary/[0.06] transition-all duration-200 h-full">
      <div className="relative aspect-video overflow-hidden">
        {course.imageUrl ? (
          <ContentImage
            src={course.imageUrl}
            alt={course.title}
            fill
            sizes="(max-width: 768px) 100vw, 50vw"
            loading={priority ? "eager" : "lazy"}
            fetchPriority={priority ? "high" : undefined}
            className="object-cover group-hover:scale-[1.03] transition-transform duration-500 ease-out"
          />
        ) : (
          <div className="w-full h-full bg-gradient-to-br from-primary/20 via-primary/10 to-secondary" />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/25 to-transparent" />
        {course.isNew && (
          <Badge className="absolute top-2 left-2 bg-emerald-500/90 text-white border-0 text-xs">
            New
          </Badge>
        )}
        {kindBadge && (
          <Badge className={cn("absolute top-2 right-2 border-0 text-xs", kindBadge.class)}>
            {kindBadge.label}
          </Badge>
        )}
        <div className="absolute inset-x-0 bottom-0 p-3 sm:p-4">
          <h3 className="line-clamp-2 text-sm sm:text-base font-bold leading-tight text-white drop-shadow-sm">
            {course.title}
          </h3>
        </div>
      </div>

      <CardContent className="p-3 sm:p-4 flex flex-col flex-1">
        <p className="text-xs text-muted-foreground mb-3 sm:mb-4 line-clamp-2 flex-1 leading-relaxed">
          {course.description}
        </p>

        <div className="mb-3 sm:mb-4">
          <div className="flex items-center justify-between text-xs mb-1.5">
            <span className="text-muted-foreground">Прогресс</span>
            <span className="text-primary font-semibold">{course.progressPercent}%</span>
          </div>
          <ProgressBar value={course.progressPercent} />
          <div className="mt-1.5 text-xs text-muted-foreground">
            {course.completedItems} из {course.totalItems}{" "}
            {pluralize(course.totalItems, "элемента", "элементов", "элементов")} завершено
          </div>
          <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[11px] tabular-nums text-muted-foreground">
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
            {course.totalQuizzes > 0 && (
              <span className="inline-flex items-center gap-1">
                <Icons.quiz className="size-3" aria-hidden />
                {course.completedQuizzes}/{course.totalQuizzes} тестов
              </span>
            )}
          </div>
          {course.lastActivityAt && (
            <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px] text-muted-foreground">
              <span>Активность {formatRelativeDate(course.lastActivityAt)}</span>
            </div>
          )}
        </div>

        <Button asChild variant="outline" className="w-full">
          <Link href={routes.courseOverview(course.courseSlug)}>Перейти к курсу</Link>
        </Button>
      </CardContent>
    </Card>
  );
}
