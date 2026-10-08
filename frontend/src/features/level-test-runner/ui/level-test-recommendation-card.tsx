"use client";

import { courseDetailQueryOptions } from "@/entities/course";
import { trackGrowthEvent } from "@/shared/analytics";
import { routes } from "@/shared/config/routes";
import { AuthorCredit } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { resolveRecommendationCta } from "../model/result-summary";

interface LevelTestRecommendationCardProps {
  testId: string;
  recommendedCourseId: string | null;
  weakestTitles: string[];
}

/**
 * Блок рекомендации: если у попытки есть `recommendedCourseId` — карточка
 * курса с переходом на него; иначе (или курс не зарезолвился) — CTA в каталог
 * «Усиль слабые места — выбери курс». Issue #481.
 */
export function LevelTestRecommendationCard({
  testId,
  recommendedCourseId,
  weakestTitles,
}: LevelTestRecommendationCardProps) {
  const courseQuery = useQuery({
    ...courseDetailQueryOptions(recommendedCourseId ?? ""),
    enabled: recommendedCourseId !== null,
    retry: false,
  });

  if (recommendedCourseId && courseQuery.isPending) {
    return (
      <div className="rounded-xl border border-border/60 bg-card p-5">
        <Skeleton className="mb-2 h-4 w-40" />
        <Skeleton className="mb-3 h-6 w-2/3" />
        <Skeleton className="h-9 w-44" />
      </div>
    );
  }

  const course = courseQuery.data ?? null;
  const cta = resolveRecommendationCta({
    recommendedCourseId,
    courseSlug: course?.slug ?? null,
  });

  if (recommendedCourseId && cta.kind === "course" && course) {
    return (
      <div className="rounded-xl border border-primary/30 bg-primary/5 p-5">
        <p className="mb-1 flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-primary">
          <Icons.target className="size-3.5" aria-hidden />
          Рекомендуем по итогам теста
        </p>
        <p className="text-lg font-semibold">{course.title}</p>
        {course.authorDisplayName && (
          <AuthorCredit
            name={course.authorDisplayName}
            avatarUrl={course.authorAvatarUrl}
            className="mt-1"
          />
        )}
        {course.description && (
          <p className="mt-1 line-clamp-2 text-sm text-muted-foreground">{course.description}</p>
        )}
        <div className="mt-4 flex flex-wrap gap-2">
          <Button asChild>
            <Link
              href={cta.href}
              onClick={() =>
                trackGrowthEvent({
                  name: "level_test_recommendation_clicked",
                  properties: { test_id: testId, course_id: recommendedCourseId },
                })
              }
            >
              Перейти к курсу
              <Icons.arrowRight className="size-4" />
            </Link>
          </Button>
          <Button asChild variant="outline">
            <Link href={routes.pricing}>
              <Icons.crown className="size-4" />
              Выбрать план доступа
            </Link>
          </Button>
        </div>
        <p className="mt-3 text-xs text-muted-foreground">
          Курс входит в планы обучения .NET Fullstack — полный доступ открывает все курсы
          направления, задания с проверкой и закрытое сообщество.
        </p>
      </div>
    );
  }

  return (
    <div className="relative overflow-hidden rounded-xl border border-primary/30 bg-gradient-to-br from-primary/10 via-card to-card p-5 sm:p-6">
      {/* Глубина: два мягких glow-пятна в палитре лендинга воронки */}
      <div
        aria-hidden
        className="pointer-events-none absolute -right-16 -top-20 size-56 rounded-full bg-primary/15 blur-3xl"
      />
      <div
        aria-hidden
        className="pointer-events-none absolute -bottom-24 -left-12 size-48 rounded-full bg-violet-500/10 blur-3xl"
      />

      <div className="relative">
        <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-primary">
          <Icons.crown className="size-3.5" aria-hidden />
          Полный доступ
        </p>
        <p className="mt-1.5 text-balance text-lg font-semibold sm:text-xl">
          Усиль слабые места — открой все курсы .NET Fullstack разом
        </p>

        {weakestTitles.length > 0 ? (
          <div className="mt-2.5 flex flex-wrap items-center gap-1.5">
            <span className="text-sm text-muted-foreground">Начни с тем:</span>
            {weakestTitles.map((title) => (
              <span
                key={title}
                className="rounded-md border border-primary/25 bg-primary/10 px-2 py-0.5 text-xs font-medium text-foreground/85"
              >
                {title}
              </span>
            ))}
          </div>
        ) : (
          <p className="mt-1 text-sm text-muted-foreground">
            Подбери курс под свой уровень — или забери всё сразу
          </p>
        )}

        <ul className="mt-4 grid gap-x-6 gap-y-1.5 text-sm sm:grid-cols-3">
          {["Все курсы .NET Fullstack", "Задания с проверкой", "Закрытое сообщество"].map(
            (benefit) => (
              <li key={benefit} className="flex items-center gap-1.5 text-foreground/85">
                <Icons.completed className="size-4 shrink-0 text-primary" aria-hidden />
                {benefit}
              </li>
            ),
          )}
        </ul>

        <div className="mt-5 flex flex-wrap gap-2">
          <Button asChild className="shadow-[0_0_28px_-8px] shadow-primary/50">
            <Link href={routes.pricing}>
              <Icons.crown className="size-4" />
              Выбрать план доступа
            </Link>
          </Button>
          <Button asChild variant="outline">
            <Link href={cta.href}>
              В каталог курсов
              <Icons.arrowRight className="size-4" />
            </Link>
          </Button>
        </div>
      </div>
    </div>
  );
}
