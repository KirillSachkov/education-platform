"use client";

import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import {
  courseCurriculumQueryOptions,
  getAdjacentLearningItems,
  getCourseItemHref,
} from "@/entities/course";
import { parseCourseViewTab } from "@/features/course-learning";
import { quizQueryOptions } from "@/entities/quiz";
import { StudentQuizPage } from "@/features/quiz-runner";
import { routes } from "@/shared/config/routes";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { CourseBreadcrumb } from "@/shared/ui/components/course-breadcrumb";
import { LessonNav } from "@/shared/ui/components/lesson-nav";

/**
 * Квиз внутри курса: breadcrumb-бар «Курс → Модуль → Квиз» над тем же
 * {@link StudentQuizPage}, что и на standalone-роуте /quizzes/[quizId].
 * Зеркалит шапку материала (course-material-view) — первый пункт крошек
 * возвращает на обзор курса.
 */
export function CourseQuizClient({ quizId }: { quizId: string }) {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();
  const searchParams = useSearchParams();
  const sectionIdParam = searchParams.get("section");

  const { data: curriculum } = useQuery(courseCurriculumQueryOptions(courseId));
  // Тот же query key, что внутри StudentQuizPage — react-query дедуплицирует.
  const { data: quiz } = useQuery(quizQueryOptions.studentQuizOptions(quizId));

  const moduleSections = (curriculum?.sections ?? []).filter(
    (section) => section.itemType === "Module",
  );
  const hostSections = moduleSections.filter((section) =>
    section.items.some((item) => item.itemType === "Quiz" && item.id === quizId),
  );
  // Квиз переиспользуется в нескольких модулях — ?section= уточняет контекст.
  const hostSection =
    hostSections.find((section) => section.id === sectionIdParam) ?? hostSections[0] ?? null;

  const breadcrumbs = [
    {
      label: curriculum?.title ?? "Курс",
      href: routes.courseOverview(courseSlug),
    },
    ...(hostSection
      ? [{ label: hostSection.title, href: routes.courseModule(courseSlug, hostSection.id) }]
      : []),
    { label: quiz?.title ?? "Тест" },
  ];

  // Prev/next within the program order — a quiz is part of the sequence now, so
  // it gets the same footer as lessons/issues instead of being a dead end.
  const tab = parseCourseViewTab(searchParams.get("tab"), "modules");
  const nav = getAdjacentLearningItems(curriculum, quizId, sectionIdParam);
  const prev = nav.previousItem
    ? {
        title: nav.previousItem.title,
        href: getCourseItemHref(courseSlug, nav.previousItem.itemType, nav.previousItem.id, { tab }),
      }
    : null;
  const next = nav.nextItem
    ? {
        title: nav.nextItem.title,
        href: getCourseItemHref(courseSlug, nav.nextItem.itemType, nav.nextItem.id, { tab }),
      }
    : null;

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex items-center gap-2 border-b px-3 py-2.5 md:px-6">
        <div className="min-w-0 flex-1 overflow-x-auto">
          <CourseBreadcrumb items={breadcrumbs} />
        </div>
      </div>
      <div className="flex-1 overflow-y-auto">
        <StudentQuizPage quizId={quizId} />
        {(prev || next) && (
          <div className="mx-auto max-w-3xl px-4 pb-10 sm:px-6">
            <LessonNav prev={prev} next={next} />
          </div>
        )}
      </div>
    </div>
  );
}
