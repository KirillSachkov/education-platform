"use client";

import {
  getMaterialAccessBadge,
  getMaterialKindBadge,
  getMaterialStatusBadge,
  materialDetailQueryOptions,
  MaterialShareButton,
} from "@/entities/material";
import {
  courseCurriculumQueryOptions,
  getAdjacentLearningItems,
  getCourseItemHref,
} from "@/entities/course";
import {
  collectionDetailQueryOptions,
  getAdjacentCollectionMaterials,
} from "@/entities/collection";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import { SearchableTagsField } from "@/entities/tag";
import {
  useMarkMaterialViewed,
  useUnmarkMaterialViewed,
  useResolvedCourseAccess,
  useTheaterMode,
  parseCourseViewTab,
} from "@/features/course-learning";
import { MaterialAuthorActions } from "@/features/materials-manage";
import { BookmarkToggleButton } from "@/entities/bookmark";
import { CommentSection } from "@/features/comments";
import { MaterialQuizBlock } from "@/features/quiz-runner";
import { useTrackMaterialView } from "@/features/track-material-view";
import { useTrackFreeMaterialEngagement } from "@/shared/analytics";
import { SuccessCheck, ViewsBadge } from "@/shared/ui/components";
import { isContentAccessError, isForbiddenError } from "@/shared/api";
import {
  resolveSecondaryUnlockHref,
  resolveUnlockHref,
  type LockReason,
} from "@/shared/lib/lock-copy";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import Link from "next/link";
import { EntityTypes } from "@/shared/config/entity-types";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { parseFromContext, routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { createHeadingSlugger, type MarkdownHeading } from "@/shared/lib/markdown-headings";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { estimateReadingMinutes, formatReadingTime } from "@/shared/lib/reading-time";
import { parseStartSeconds } from "@/shared/lib/video-deep-link";
import { CourseBreadcrumb } from "@/shared/ui/components/course-breadcrumb";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { Badge } from "@/shared/ui/kit/badge";
import { Card } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { useSession } from "next-auth/react";
import dynamic from "next/dynamic";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { LessonNav } from "@/shared/ui/components/lesson-nav";

const VideoPlayerWithChapters = dynamic(
  () =>
    import("@/features/video-chapters").then((m) => ({
      default: m.VideoPlayerWithChapters,
    })),
  {
    ssr: false,
    loading: () => <div className="h-64 animate-pulse rounded-md bg-muted" />,
  },
);

interface CourseMaterialViewProps {
  courseId: string;
  materialId: string;
  /** Optional module id — enables "mark viewed" button */
  moduleId?: string;
}

export function CourseMaterialView({
  courseId,
  materialId,
  moduleId: _moduleId,
}: CourseMaterialViewProps) {
  const { data: session } = useSession();
  const currentUserId = session?.user?.id;
  const courseSlug = useCourseSlug();
  const router = useRouter();
  const searchParams = useSearchParams();
  const contentRef = useRef<HTMLDivElement | null>(null);

  const { data: curriculum, isLoading: isCurriculumLoading } = useQuery(
    courseCurriculumQueryOptions(courseId),
  );
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: material, isLoading, error } = useQuery(materialDetailQueryOptions(materialId));
  const { data: learningState } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });
  const materialProgress = learningState?.materials?.find((m) => m.materialId === materialId);
  const isCompleted = materialProgress?.status === "VIEWED";
  const markMaterialViewedMutation = useMarkMaterialViewed(courseId);
  const unmarkMaterialViewedMutation = useUnmarkMaterialViewed(courseId);
  const isViewToggleBusy =
    markMaterialViewedMutation.isPending || unmarkMaterialViewedMutation.isPending;
  const theater = useTheaterMode();

  // Silent fire-and-forget трекер «N просмотров» (issue #234). На course-view запускается
  // только для доступных материалов — нет смысла считать клик в заблокированный.
  useTrackMaterialView({
    materialId,
    courseId,
    isAccessible: material?.isAccessible ?? false,
  });
  useTrackFreeMaterialEngagement({
    materialId,
    courseId,
    enabled: material?.isAccessible === true && material.accessType === "PUBLIC",
  });

  const sectionId = searchParams.get("section");
  const tab = parseCourseViewTab(searchParams.get("tab"), "modules");
  const navigation = getAdjacentLearningItems(curriculum, materialId, sectionId);

  // Когда юзер пришёл из подборки (?from=collection:slug:courseSlug:collectionId),
  // запоминаем её, чтобы заменить section-крошку на подборку (#276). Хук вызываем
  // до early-return'ов по contract'у react-hooks/rules-of-hooks.
  const fromContext = parseFromContext(searchParams.get("from"));
  const collectionFromRaw =
    fromContext?.kind === "collection" && fromContext.courseSlug === courseSlug
      ? fromContext
      : null;
  const collectionContextId = collectionFromRaw?.collectionId ?? null;
  const { data: collectionFromContext } = useQuery({
    ...collectionDetailQueryOptions(collectionContextId ?? ""),
    enabled: !!collectionContextId,
  });

  // Пришли из подборки и материал в ней найден → prev/next (карточки + хоткеи)
  // ходят по подборке, сохраняя from-контекст (#464); иначе — по программе курса.
  const collectionNav = getAdjacentCollectionMaterials(collectionFromContext, materialId);
  const collectionNavFrom = collectionNav.currentItem ? collectionFromRaw : null;

  const previousItem = collectionNavFrom
    ? collectionNav.previousItem
      ? {
          id: collectionNav.previousItem.materialId,
          title: collectionNav.previousItem.title,
          href: routes.courseMaterial(courseSlug, collectionNav.previousItem.materialId, {
            from: collectionNavFrom,
          }),
        }
      : null
    : navigation.previousItem
      ? {
          id: navigation.previousItem.id,
          title: navigation.previousItem.title,
          href: getCourseItemHref(
            courseSlug,
            navigation.previousItem.itemType,
            navigation.previousItem.id,
            { tab },
          ),
          itemType: navigation.previousItem.itemType,
          sectionTitle: navigation.previousItem.sectionTitle,
        }
      : null;

  const nextItem = collectionNavFrom
    ? collectionNav.nextItem
      ? {
          id: collectionNav.nextItem.materialId,
          title: collectionNav.nextItem.title,
          href: routes.courseMaterial(courseSlug, collectionNav.nextItem.materialId, {
            from: collectionNavFrom,
          }),
        }
      : null
    : navigation.nextItem
      ? {
          id: navigation.nextItem.id,
          title: navigation.nextItem.title,
          href: getCourseItemHref(
            courseSlug,
            navigation.nextItem.itemType,
            navigation.nextItem.id,
            {
              tab,
            },
          ),
          itemType: navigation.nextItem.itemType,
          sectionTitle: navigation.nextItem.sectionTitle,
        }
      : null;

  const prevHref = previousItem?.href ?? null;
  const nextHref = nextItem?.href ?? null;

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      // Skip when user is typing in an input/textarea
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;

      if (e.altKey && e.shiftKey && e.key === "ArrowLeft" && prevHref) {
        e.preventDefault();
        router.push(prevHref);
      } else if (e.altKey && e.shiftKey && e.key === "ArrowRight" && nextHref) {
        e.preventDefault();
        router.push(nextHref);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [prevHref, nextHref, router]);

  const [headings, setHeadings] = useState<MarkdownHeading[]>([]);
  const [activeHeadingId, setActiveHeadingId] = useState<string | null>(null);

  const handleHeadingClick = (headingId: string) => {
    const element = document.getElementById(headingId);
    if (!element) return;
    setActiveHeadingId(headingId);
    element.scrollIntoView({ behavior: "smooth", block: "start" });
    window.history.replaceState(null, "", `#${headingId}`);
  };

  useEffect(() => {
    if (!material?.content || !contentRef.current) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setHeadings([]);
      return;
    }

    const slugger = createHeadingSlugger();
    const nextHeadings = Array.from(
      contentRef.current.querySelectorAll<HTMLHeadingElement>("h2, h3"),
    )
      .map((element) => {
        const text = element.textContent?.trim() ?? "";
        if (!text) return null;
        const id = slugger.next(text);
        element.id = id;
        return {
          id,
          level: Number(element.tagName.slice(1)),
          text,
        } satisfies MarkdownHeading;
      })
      .filter((h): h is MarkdownHeading => h !== null);

    setHeadings(nextHeadings);
    setActiveHeadingId(nextHeadings[0]?.id ?? null);
  }, [material?.content]);

  useEffect(() => {
    if (headings.length === 0 || !contentRef.current) return;

    const headingElements = headings
      .map((h) => document.getElementById(h.id))
      .filter((el): el is HTMLElement => el !== null);

    if (headingElements.length === 0) return;

    const scrollContainer = contentRef.current.closest(
      "[data-app-scroll-container]",
    ) as HTMLElement | null;

    const updateActiveHeading = () => {
      const containerTop = scrollContainer?.getBoundingClientRect().top ?? 0;
      const threshold = containerTop + 120;
      let currentHeading = headingElements[0];
      for (const element of headingElements) {
        if (element.getBoundingClientRect().top <= threshold) {
          currentHeading = element;
        } else {
          break;
        }
      }
      setActiveHeadingId(currentHeading.id);
    };

    updateActiveHeading();

    const target: HTMLElement | Window = scrollContainer ?? window;
    target.addEventListener("scroll", updateActiveHeading, { passive: true });
    window.addEventListener("resize", updateActiveHeading);

    return () => {
      target.removeEventListener("scroll", updateActiveHeading);
      window.removeEventListener("resize", updateActiveHeading);
    };
  }, [headings]);

  if (isLoading || isCurriculumLoading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Icons.loading className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  // 401/403 — показываем мягкий lock-CTA, а не ErrorCard (паттерн collection-view).
  if (error && (isForbiddenError(error) || isContentAccessError(error))) {
    // не-зачисленный auth → not_enrolled, аноним → anonymous.
    const lockReason: LockReason =
      access.accessLevel === "anonymous" ? "anonymous" : "not_enrolled";
    return (
      <MaterialAccessLocked
        lockReason={lockReason}
        courseSlug={courseSlug}
        courseTitle={curriculum?.title}
      />
    );
  }
  if (error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  if (!material) {
    return (
      <NotFoundFallback
        message="Материал не найден"
        backHref={routes.courseOverview(courseSlug)}
        backLabel="К курсу"
      />
    );
  }

  const kindBadge = getMaterialKindBadge(material.kind);
  const statusBadge = getMaterialStatusBadge(material.status);
  const accessBadge = getMaterialAccessBadge(material.accessType);
  const isOwner = !!currentUserId && material.authorId === currentUserId;

  const currentSection = navigation.currentItem;
  const sectionHref = currentSection
    ? currentSection.sectionType === "Module"
      ? routes.courseModule(courseSlug, currentSection.sectionId)
      : routes.courseProject(courseSlug, currentSection.sectionId)
    : undefined;
  const collectionCrumb = collectionContextId
    ? {
        label: collectionFromContext?.title ?? "Подборка",
        href: routes.courseCollectionDetail(courseSlug, collectionContextId),
      }
    : null;
  const breadcrumbs = [
    {
      label: curriculum?.title ?? "Курс",
      href: routes.courseOverview(courseSlug),
    },
    ...(collectionCrumb
      ? [collectionCrumb]
      : currentSection
        ? [{ label: currentSection.sectionTitle, href: sectionHref }]
        : []),
    { label: material.title },
  ];

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div aria-hidden="true" className="scroll-progress-bar" />
      <div className="flex items-center gap-2 border-b px-3 py-2.5 md:px-6">
        <div className="min-w-0 flex-1 md:overflow-x-auto">
          {/* <md — компактная «← родитель»-ссылка вместо полного trail'а (#511). */}
          <CourseBreadcrumb items={breadcrumbs} compactOnMobile />
        </div>
        {isOwner && (
          <div className="flex items-center gap-2 shrink-0">
            <MaterialAuthorActions
              material={material}
              onDeletedHref={sectionHref ?? routes.courseOverview(courseSlug)}
            />
          </div>
        )}
      </div>

      <div className="flex-1 overflow-y-auto">
        <div
          className={cn(
            "mx-auto pt-4 sm:pt-10",
            theater.isTheaterMode && !theater.isMobile
              ? "max-w-[calc((100svh-12rem)*16/9)] px-2 sm:px-4"
              : "max-w-6xl px-3 sm:px-6",
          )}
        >
          <div className="mb-3 flex flex-wrap items-center gap-2 sm:mb-4 sm:gap-3">
            <Badge variant="outline" className={kindBadge.className}>
              {kindBadge.label}
            </Badge>
            <Badge variant="outline" className={accessBadge.className}>
              {accessBadge.label}
            </Badge>
            {isOwner && (
              <Badge variant="outline" className={statusBadge.className}>
                {statusBadge.label}
              </Badge>
            )}
            <ViewsBadge count={material.viewsCount ?? 0} variant="pill" />
            {/* «N мин чтения» — только у текстовых материалов; у видео вместо него длительность (#500). */}
            {(material.kind === "ARTICLE" || material.kind === "NOTE") &&
              (() => {
                const minutes = estimateReadingMinutes(material.content);
                if (minutes === 0) return null;
                return <Badge variant="outline">{formatReadingTime(minutes)} чтения</Badge>;
              })()}
            {material.video?.durationSeconds ? (
              <Badge variant="outline" className="gap-1 tabular-nums">
                <Icons.clock className="size-3" />
                {formatDurationSecondsHuman(material.video.durationSeconds)}
              </Badge>
            ) : null}
          </div>

          <div className="mb-6 flex flex-col gap-3 sm:mb-8 sm:flex-row sm:items-start">
            <h1
              className="flex-1 text-xl font-bold leading-snug break-words sm:text-2xl"
              style={{ viewTransitionName: "material-heading" }}
            >
              {material.title}
            </h1>
            <div className="flex items-center gap-2 shrink-0">
              <MaterialShareButton
                materialId={materialId}
                url={routes.courseMaterial(courseSlug, materialId)}
                title={material.title}
                className="h-9"
              />
              {material.video?.externalVideoId && !theater.isMobile && (
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-9 hidden sm:inline-flex"
                  onClick={theater.toggle}
                  aria-label={theater.isTheaterMode ? "Обычный режим" : "Широкий режим"}
                  title={theater.isTheaterMode ? "Обычный режим" : "Широкий режим"}
                >
                  {theater.isTheaterMode ? (
                    <Icons.theaterModeExit className="size-4" />
                  ) : (
                    <Icons.theaterMode className="size-4" />
                  )}
                </Button>
              )}
              <BookmarkToggleButton
                courseId={courseId}
                entityType="Material"
                entityId={materialId}
              />
              {access.isAuthenticated && (
                <Button
                  variant={isCompleted ? "secondary" : "default"}
                  size="sm"
                  className="gap-1.5 h-9 ml-auto sm:ml-0"
                  // isCompleted flips optimistically via mark/unmark hooks,
                  // so the button immediately reflects the new state. Spinner
                  // appears only while a request is pending, to avoid letting
                  // the user spam toggle and race the cascade.
                  disabled={!access.hasActiveEnrollment || isViewToggleBusy}
                  onClick={() =>
                    isCompleted
                      ? unmarkMaterialViewedMutation.mutate({ materialId })
                      : markMaterialViewedMutation.mutate({ materialId })
                  }
                  title={isCompleted ? "Снять отметку" : "Отметить изученным"}
                >
                  {isViewToggleBusy ? (
                    <Icons.loading className="size-4 animate-spin" />
                  ) : isCompleted ? (
                    // Mounts on the false→true mark → plays the success-check
                    // celebration; unmounts on unmark back to the outline icon.
                    <SuccessCheck subtle className="size-4" />
                  ) : (
                    <Icons.completed className="size-4" />
                  )}
                  <span className="sm:hidden">{isCompleted ? "Изучено" : "Изучил"}</span>
                  <span className="hidden sm:inline">
                    {isCompleted ? "Изучено" : "Отметить изученным"}
                  </span>
                </Button>
              )}
            </div>
          </div>
        </div>

        {/* Видео — в theater-режиме вырывается из узкого контейнера и
            ограничивается max-h ~85vh, поддерживая 16:9. */}
        {material.video?.externalVideoId && (
          <div
            className={cn(
              "mx-auto mb-6",
              theater.isTheaterMode && !theater.isMobile
                ? "max-w-[calc((100svh-12rem)*16/9)] px-2 sm:px-4"
                : "max-w-6xl px-3 sm:px-6",
            )}
          >
            <VideoPlayerWithChapters
              videoId={material.video.externalVideoId}
              posterUrl={material.imageUrl ?? undefined}
              chapters={material.chapters}
              startSeconds={parseStartSeconds(searchParams.get("t"))}
            />
          </div>
        )}

        <div className="mx-auto max-w-6xl px-3 pb-4 sm:px-6 sm:pb-10">
          {/* Тэги — компактные, под видео; не конкурируют визуально с kind/access-бейджами над заголовком. */}
          <SearchableTagsField
            entityId={materialId}
            entityType={EntityTypes.MATERIAL}
            readOnly
            className="mb-6 sm:mb-8 gap-1.5 [&>button]:px-2 [&>button]:py-0.5 [&>button]:text-[11px] [&>span]:px-2 [&>span]:py-0.5 [&>span]:text-[11px]"
          />

          {headings.length > 0 && (
            <Card className="mb-8 gap-4 border-border/70 bg-card/80 p-5 xl:hidden">
              <div className="flex items-center gap-2 text-sm font-semibold">
                <Icons.listTree size={16} className="text-primary" />
                Содержание
              </div>
              <nav className="space-y-1">
                {headings.map((heading) => (
                  <button
                    type="button"
                    key={heading.id}
                    onClick={() => handleHeadingClick(heading.id)}
                    className={cn(
                      "block w-full rounded-lg px-2 py-1 text-left text-sm leading-snug transition-colors",
                      activeHeadingId === heading.id
                        ? "bg-primary/10 font-medium text-primary"
                        : "text-muted-foreground hover:text-foreground",
                    )}
                    style={{
                      paddingLeft: heading.level === 3 ? "1.5rem" : undefined,
                    }}
                  >
                    {heading.text}
                  </button>
                ))}
              </nav>
            </Card>
          )}

          <div className="grid gap-10 xl:grid-cols-[minmax(0,1fr)_18rem]">
            <div className="min-w-0 max-w-4xl">
              {material.description && material.description.trim().length > 0 && (
                <section className="mb-8 border-b border-border/60 pb-8">
                  <div className="mb-3 flex items-center gap-2 text-sm font-semibold">
                    <Icons.document size={16} className="text-primary" />
                    Описание
                  </div>
                  <MarkdownContent className="prose-lesson">{material.description}</MarkdownContent>
                </section>
              )}

              <div ref={contentRef}>
                {material.content ? (
                  <MarkdownContent className="prose-lesson">{material.content}</MarkdownContent>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    В этом материале пока нет содержимого.
                  </p>
                )}
              </div>
            </div>

            {headings.length > 0 && (
              <aside className="hidden self-start xl:sticky xl:top-6 xl:block">
                <Card className="gap-4 border-border/70 bg-card/80 p-5">
                  <div className="flex items-center gap-2 text-sm font-semibold">
                    <Icons.listTree size={16} className="text-primary" />
                    Содержание
                  </div>
                  <nav className="max-h-[calc(100svh-9rem)] space-y-1 overflow-y-auto pr-1">
                    {headings.map((heading) => (
                      <button
                        type="button"
                        key={heading.id}
                        onClick={() => handleHeadingClick(heading.id)}
                        className={cn(
                          "block w-full rounded-lg px-2 py-1 text-left text-sm leading-snug transition-colors",
                          activeHeadingId === heading.id
                            ? "bg-primary/10 font-medium text-primary"
                            : "text-muted-foreground hover:text-foreground",
                        )}
                        style={{
                          paddingLeft: heading.level === 3 ? "1.5rem" : undefined,
                        }}
                      >
                        {heading.text}
                      </button>
                    ))}
                  </nav>
                </Card>
              </aside>
            )}
          </div>

          {access.isAuthenticated && (
            <MaterialQuizBlock
              materialId={materialId}
              className="mt-10 border-t border-border/60 pt-6"
            />
          )}

          <LessonNav prev={previousItem} next={nextItem} />

          <div className="mt-10 border-t border-border/60 pt-4 text-xs text-muted-foreground/60">
            Опубликовано {formatShortDateWithTime(material.createdAt)}
          </div>

          <CommentSection
            targetType={EntityTypes.MATERIAL}
            targetId={materialId}
            className="mt-6 border-t border-border/60 pt-8"
          />

          <div className="h-12" />
        </div>
      </div>
    </div>
  );
}

/**
 * 401 (аноним на non-PUBLIC) или 403 (залогинен без grant'а) — мягкий inline
 * lock-callout вместо full-screen экрана. Юзер видит шапку с back-кнопкой,
 * курсом-контекстом и LockCallout по центру; браузерный «назад» возвращает
 * туда, откуда пришёл. Без редиректа на /login.
 */
function MaterialAccessLocked({
  lockReason,
  courseSlug,
  courseTitle,
}: {
  lockReason: LockReason;
  courseSlug: string;
  courseTitle?: string | null;
}) {
  const returnTo = typeof window !== "undefined" ? window.location.pathname : null;
  const ctaHref = resolveUnlockHref({ lockReason, returnTo });
  const secondaryHref = resolveSecondaryUnlockHref({ lockReason });
  const backHref = routes.courseOverview(courseSlug);
  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div aria-hidden="true" className="scroll-progress-bar" />
      <div className="flex items-center gap-2 border-b px-3 py-2.5 md:px-6">
        <Link
          href={backHref}
          className="inline-flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <Icons.chevronLeft className="size-3.5" />К курсу{courseTitle ? ` «${courseTitle}»` : ""}
        </Link>
      </div>
      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto flex max-w-md flex-col items-stretch px-4 py-12 sm:py-16">
          <div className="rounded-2xl border border-border/60 bg-card/95 p-5 shadow-xl shadow-black/20">
            <LockCallout
              reason={lockReason}
              courseTitle={courseTitle ?? null}
              ctaHref={ctaHref}
              secondaryCtaHref={secondaryHref}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
