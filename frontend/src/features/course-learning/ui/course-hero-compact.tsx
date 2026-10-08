"use client";

import type { CourseCurriculumDto } from "@/entities/course";
import type { CourseLearningStateDto } from "@/entities/course-progress";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { ContentImage } from "@/shared/ui/components";
import Link from "next/link";

interface CourseHeroCompactProps {
  course: CourseCurriculumDto;
  learningState: CourseLearningStateDto;
  continueHref: string | null;
}

/**
 * Compact hero for enrolled view — title + progress + primary CTA.
 * Designed to take much less vertical space than the full marketing hero.
 */
export function CourseHeroCompact({
  course,
  learningState,
  continueHref,
}: CourseHeroCompactProps) {
  const { progressPercent } = learningState.summary;

  return (
    <Card className="relative overflow-hidden border-border/60 py-0 shadow-md shadow-black/5">
      {course.imageUrl && (
        <div className="absolute inset-0 pointer-events-none" aria-hidden="true">
          <ContentImage
            src={course.imageUrl}
            alt=""
            fill
            sizes="100vw"
            className="object-cover scale-110 opacity-20 blur-2xl"
          />
          <div className="absolute inset-0 bg-gradient-to-r from-card via-card/95 to-card/80" />
        </div>
      )}
      <div
        className="absolute -top-16 -right-16 size-48 rounded-full bg-primary/10 blur-3xl pointer-events-none"
        aria-hidden="true"
      />

      <div className="relative flex flex-col gap-4 p-4 md:flex-row md:items-center md:gap-6 md:p-5">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h1
              className="text-lg md:text-xl font-semibold tracking-tight text-foreground truncate"
              style={{ viewTransitionName: `course-${course.id}-title` }}
            >
              {course.title}
            </h1>
            {course.isNew && (
              <Badge className="bg-emerald-500/90 text-white border-0 text-[10px] shrink-0 px-1.5">
                New
              </Badge>
            )}
          </div>
          {course.description && (
            <p className="text-xs md:text-sm text-muted-foreground mt-1 line-clamp-2">
              {course.description}
            </p>
          )}

          <div className="flex items-center gap-3 mt-3">
            <div className="relative h-1.5 flex-1 rounded-full bg-secondary/70 overflow-hidden">
              <div
                className={cn(
                  "absolute inset-y-0 left-0 rounded-full transition-[width] duration-700",
                  progressPercent === 100
                    ? "bg-gradient-to-r from-green/80 to-green"
                    : "bg-gradient-to-r from-primary/70 to-primary",
                )}
                style={{ width: `${progressPercent}%` }}
              />
            </div>
            <span className="text-xs font-semibold tabular-nums text-foreground shrink-0">
              {progressPercent}%
            </span>
          </div>
        </div>

        {continueHref && (
          <Button
            asChild
            size="sm"
            className="shrink-0 h-10 gap-2 rounded-xl bg-primary/15 text-primary border border-primary/30 hover:bg-primary/25 hover:border-primary/50 hover:text-primary"
          >
            <Link href={continueHref}>
              <Icons.playCircle size={16} />
              Продолжить обучение
            </Link>
          </Button>
        )}
      </div>
    </Card>
  );
}
