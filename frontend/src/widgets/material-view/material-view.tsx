"use client";

import {
  getMaterialAccessBadge,
  getMaterialKindBadge,
  getMaterialStatusBadge,
  materialBindingsQueryOptions,
  materialDetailQueryOptions,
  MaterialShareButton,
  type MaterialDetailDto,
} from "@/entities/material";
import {
  collectionDetailQueryOptions,
  getAdjacentCollectionMaterials,
} from "@/entities/collection";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { SearchableTagsField } from "@/entities/tag";
import { CommentSection } from "@/features/comments";
import { useMarkMaterialViewed, useUnmarkMaterialViewed } from "@/features/course-learning";
import { MaterialNotesBlock } from "@/features/material-notes";
import { MaterialQuizBlock } from "@/features/quiz-runner";
import { MaterialAuthorActions } from "@/features/materials-manage";
import { useTrackMaterialView } from "@/features/track-material-view";
import { useTrackFreeMaterialEngagement } from "@/shared/analytics";
import { AuthorCredit, ViewsBadge } from "@/shared/ui/components";
import { isContentAccessError, isForbiddenError } from "@/shared/api";
import {
  resolveSecondaryUnlockHref,
  resolveUnlockHref,
  type LockReason,
} from "@/shared/lib/lock-copy";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import { useIsAuthenticated } from "@/shared/auth";
import { EntityTypes } from "@/shared/config/entity-types";
import { decodeFromContext, parseFromContext, routes } from "@/shared/config/routes";
import { useSearchParams } from "next/navigation";
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
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { useQuery } from "@tanstack/react-query";
import { Icons } from "@/shared/ui/icons";
import dynamic from "next/dynamic";
import Link from "next/link";
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

interface MaterialViewProps {
  materialId: string;
  mode: "learning" | "teaching";
  /** PUBLIC anonymous snapshot for server-rendered initial HTML. */
  initialMaterial?: MaterialDetailDto | null;
  /** Overrides the default "back to list" link destination. */
  backHref?: string;
}

export function MaterialView({
  materialId,
  mode,
  initialMaterial,
  backHref: backHrefOverride,
}: MaterialViewProps) {
  const isTeaching = mode === "teaching";
  const isLearning = !isTeaching;
  const isAuthenticated = useIsAuthenticated();
  const contentRef = useRef<HTMLDivElement | null>(null);
  const {
    data: material,
    isLoading,
    error,
  } = useQuery({
    ...materialDetailQueryOptions(materialId),
    // Even PUBLIC content can differ for an author/admin. Never let the
    // anonymous server snapshot suppress the authenticated request.
    ...(initialMaterial ? { initialData: initialMaterial, initialDataUpdatedAt: 0 } : {}),
  });
  // Containment-блок «Содержится в» (курс/модуль/подборка) показывается всем читателям —
  // endpoint открыт для anon с #500 (PUBLISHED-only метаданные). Грузим после материала,
  // чтобы не стоять на critical path первого рендера.
  const { data: bindings } = useQuery({
    ...materialBindingsQueryOptions(materialId),
    enabled: !!material,
    staleTime: 5 * 60_000,
  });

  // Один факт просмотра на пару (user, material) — батч-endpoint переиспользуется
  // для single-lookup'а, дедуп через sort+distinct внутри queryOptions.
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions([materialId]),
    enabled: isLearning && isAuthenticated,
  });
  const isViewed = viewedMap?.get(materialId)?.isViewed ?? false;
  const markViewedMutation = useMarkMaterialViewed();
  const unmarkViewedMutation = useUnmarkMaterialViewed();
  const isViewToggleBusy = markViewedMutation.isPending || unmarkViewedMutation.isPending;

  // Silent fire-and-forget — заполняет material_views (auth) или
  // anonymous_material_views (anon-cookie). Issue #234.
  useTrackMaterialView({
    materialId,
    isAccessible: isLearning && (material?.isAccessible ?? false),
  });
  useTrackFreeMaterialEngagement({
    materialId,
    enabled: isLearning && material?.isAccessible === true && material.accessType === "PUBLIC",
  });

  const searchParams = useSearchParams();
  const fromContext = decodeFromContext(searchParams.get("from"));
  const fromContextRaw = parseFromContext(searchParams.get("from"));
  const backHref =
    backHrefOverride ??
    fromContext?.href ??
    (isTeaching ? routes.authorKnowledgeBase : routes.knowledgeBase);
  const backLabel = fromContext?.label ?? "Назад к списку";

  // Учитываем подборку как родителя в breadcrumb, если юзер пришёл с её страницы
  // (#276). Хук вызываем до early-return'ов по contract'у rules-of-hooks.
  const collectionFromRaw =
    !isTeaching && fromContextRaw?.kind === "collection" && !fromContextRaw.courseSlug
      ? fromContextRaw
      : null;
  const collectionContextId = collectionFromRaw?.collectionId ?? null;
  const { data: collectionFromContext } = useQuery({
    ...collectionDetailQueryOptions(collectionContextId ?? ""),
    enabled: !!collectionContextId,
  });

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

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Icons.loading className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  // 401/403 — мягкий lock-экран (паттерн course-material-view + collection-view).
  if (!material && error && (isForbiddenError(error) || isContentAccessError(error))) {
    return <StandaloneMaterialAccessLocked isAuthenticated={isAuthenticated} backHref={backHref} />;
  }

  if (!material && error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  if (!material) {
    return (
      <NotFoundFallback
        message="Материал не найден"
        backHref={backHref}
        backLabel="К списку материалов"
      />
    );
  }

  const kindBadge = getMaterialKindBadge(material.kind);
  const statusBadge = getMaterialStatusBadge(material.status);
  const accessBadge = getMaterialAccessBadge(material.accessType);

  const collectionCrumb = collectionContextId
    ? {
        label: collectionFromContext?.title ?? "Подборка",
        href: routes.collectionDetail(collectionContextId),
      }
    : null;

  // «Содержится в» — курсы (с модулем, если известен) и подборки, в которых живёт
  // материал (#500). PUBLISHED-only метаданные из bindings-эндпоинта.
  const containmentCourses = bindings?.courses ?? [];
  const containmentCollections = bindings?.collections ?? [];
  const moduleByCourseId = new Map((bindings?.modules ?? []).map((m) => [m.courseId, m] as const));
  const hasContainment = containmentCourses.length > 0 || containmentCollections.length > 0;

  // Prev/next по подборке, из которой пришли (#464): плоский порядок секций,
  // ссылки сохраняют from-контекст (можно пройти подборку насквозь), замокнутые
  // материалы не пропускаются — замок покажет целевая страница. Материал не
  // найден / подборка не загрузилась → оба null, карточки не рендерятся.
  const collectionNav = getAdjacentCollectionMaterials(collectionFromContext, materialId);
  const collectionPrevItem =
    collectionFromRaw && collectionNav.previousItem
      ? {
          id: collectionNav.previousItem.materialId,
          title: collectionNav.previousItem.title,
          href: routes.materialDetail(collectionNav.previousItem.materialId, {
            from: collectionFromRaw,
          }),
        }
      : null;
  const collectionNextItem =
    collectionFromRaw && collectionNav.nextItem
      ? {
          id: collectionNav.nextItem.materialId,
          title: collectionNav.nextItem.title,
          href: routes.materialDetail(collectionNav.nextItem.materialId, {
            from: collectionFromRaw,
          }),
        }
      : null;

  const breadcrumbs = isTeaching
    ? [{ label: "База знаний", href: routes.authorKnowledgeBase }, { label: material.title }]
    : [
        { label: "База знаний", href: routes.knowledgeBase },
        ...(collectionCrumb ? [collectionCrumb] : []),
        { label: material.title },
      ];

  return (
    <div className="flex min-h-full flex-col">
      <div aria-hidden="true" className="scroll-progress-bar" />
      {/* Мобильный top bar (#511): back-ссылка (уважает from-контекст) на своём
          ряду + ряд действий; полный crumb-trail только с md — на 390px он
          сжимался в бесполезные огрызки, а заголовок читается в h1 ниже. */}
      <div className="flex flex-col gap-2 border-b border-border/70 px-3 py-3 md:px-5">
        <Link
          href={backHref}
          className="inline-flex max-w-full items-center self-start text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <span className="truncate">← {backLabel}</span>
        </Link>
        <div className="flex w-full items-center justify-between gap-4">
          <div className="hidden min-w-0 flex-1 overflow-x-auto md:block">
            <CourseBreadcrumb items={breadcrumbs} />
          </div>
          <div className="ml-auto flex items-center gap-2 shrink-0">
            <MaterialShareButton
              materialId={materialId}
              url={routes.knowledgeBaseMaterial(materialId)}
              title={material.title}
            />
            {isLearning && isAuthenticated && (
              <Button
                variant={isViewed ? "secondary" : "default"}
                size="sm"
                className="gap-1.5 h-8"
                disabled={isViewToggleBusy}
                onClick={() =>
                  isViewed
                    ? unmarkViewedMutation.mutate({ materialId })
                    : markViewedMutation.mutate({ materialId })
                }
                title={isViewed ? "Снять отметку" : "Отметить изученным"}
              >
                {isViewToggleBusy ? (
                  <Icons.loading className="size-3.5 animate-spin" />
                ) : (
                  <Icons.completed className="size-3.5" />
                )}
                {/* Компактный лейбл на <sm — как в course-material-view (#511). */}
                <span className="sm:hidden">{isViewed ? "Изучено" : "Изучил"}</span>
                <span className="hidden sm:inline">
                  {isViewed ? "Изучено" : "Отметить изученным"}
                </span>
              </Button>
            )}
            {isTeaching && <MaterialAuthorActions material={material} />}
          </div>
        </div>
      </div>

      <div className="flex-1">
        <div className="mx-auto max-w-6xl px-6 py-10">
          <div className="mb-3 flex flex-wrap items-center gap-1.5">
            <Badge variant="outline" className={cn(kindBadge.className, "text-[11px]")}>
              {kindBadge.label}
            </Badge>
            <Badge variant="outline" className={cn(accessBadge.className, "text-[11px]")}>
              {accessBadge.label}
            </Badge>
            {isTeaching && (
              <Badge variant="outline" className={cn(statusBadge.className, "text-[11px]")}>
                {statusBadge.label}
              </Badge>
            )}
            <ViewsBadge count={material.viewsCount ?? 0} variant="pill" className="text-[11px]" />
            {/* «N мин чтения» — только у текстовых материалов; у видео вместо него длительность (#500). */}
            {(material.kind === "ARTICLE" || material.kind === "NOTE") &&
              (() => {
                const minutes = estimateReadingMinutes(material.content);
                if (minutes === 0) return null;
                return (
                  <Badge variant="outline" className="text-[11px]">
                    {formatReadingTime(minutes)} чтения
                  </Badge>
                );
              })()}
            {material.video?.durationSeconds ? (
              <Badge variant="outline" className="gap-1 text-[11px] tabular-nums">
                <Icons.clock className="size-3" />
                {formatDurationSecondsHuman(material.video.durationSeconds)}
              </Badge>
            ) : null}
          </div>

          <h1
            className={cn(
              "text-2xl font-bold leading-tight sm:text-3xl md:text-4xl",
              material.authorDisplayName || hasContainment ? "mb-4" : "mb-8",
            )}
            style={{ viewTransitionName: "material-heading" }}
          >
            {material.title}
          </h1>

          {material.authorDisplayName && (
            <AuthorCredit
              name={material.authorDisplayName}
              avatarUrl={material.authorAvatarUrl}
              size="md"
              className={cn(hasContainment ? "mb-3" : "mb-8")}
            />
          )}

          {hasContainment && (
            <div className="mb-8 flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
              <span className="mr-0.5">Содержится в</span>
              {containmentCourses.map((course) => {
                const moduleBinding = moduleByCourseId.get(course.courseId);
                return (
                  <Link
                    key={`course-${course.courseId}`}
                    href={
                      moduleBinding
                        ? routes.courseModule(course.slug, moduleBinding.moduleId)
                        : routes.courseOverview(course.slug)
                    }
                    className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-3 py-1 text-xs text-muted-foreground transition-colors hover:border-border hover:text-foreground"
                  >
                    <Icons.course className="size-3.5 shrink-0 text-primary" />
                    <span className="max-w-56 truncate">{course.title}</span>
                    {moduleBinding && (
                      <span className="max-w-40 truncate text-muted-foreground/70">
                        · {moduleBinding.title}
                      </span>
                    )}
                  </Link>
                );
              })}
              {containmentCollections.map((collection) => {
                const href = collection.courseSlug
                  ? routes.courseCollectionDetail(collection.courseSlug, collection.collectionId)
                  : routes.collectionDetail(collection.collectionId);
                return (
                  <Link
                    key={`collection-${collection.collectionId}`}
                    href={href}
                    className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-3 py-1 text-xs text-muted-foreground transition-colors hover:border-border hover:text-foreground"
                  >
                    <Icons.library className="size-3.5 shrink-0 text-primary" />
                    <span className="max-w-56 truncate">{collection.title}</span>
                  </Link>
                );
              })}
            </div>
          )}

          {material.video?.externalVideoId && (
            <div className="mb-6">
              <VideoPlayerWithChapters
                videoId={material.video.externalVideoId}
                posterUrl={material.imageUrl ?? undefined}
                chapters={material.chapters}
                startSeconds={parseStartSeconds(searchParams.get("t"))}
              />
            </div>
          )}

          {/* Компактные теги под видео (см. course-material-view: единый паттерн). */}
          <SearchableTagsField
            entityId={materialId}
            entityType={EntityTypes.MATERIAL}
            readOnly
            className="mb-8 gap-1.5 [&>button]:px-2 [&>button]:py-0.5 [&>button]:text-[11px] [&>span]:px-2 [&>span]:py-0.5 [&>span]:text-[11px]"
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

          <div
            className={cn(
              "grid gap-10",
              headings.length > 0 ? "xl:grid-cols-[minmax(0,1fr)_18rem]" : "mx-auto max-w-4xl",
            )}
          >
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

          {isLearning && isAuthenticated && (
            <MaterialQuizBlock
              materialId={materialId}
              className="mt-10 border-t border-border/60 pt-6"
            />
          )}

          {isLearning && isAuthenticated && (
            <MaterialNotesBlock
              materialId={materialId}
              className="mt-10 border-t border-border/60 pt-6"
            />
          )}

          <LessonNav prev={collectionPrevItem} next={collectionNextItem} />

          <div className="mt-10 border-t border-border/60 pt-4 text-xs text-muted-foreground/60">
            Опубликовано {formatShortDateWithTime(material.createdAt)}
          </div>

          {isLearning && (
            <CommentSection
              targetType={EntityTypes.MATERIAL}
              targetId={materialId}
              className="mt-6 border-t border-border/60 pt-8"
            />
          )}
        </div>
      </div>
    </div>
  );
}

// Silence unused — Icons re-exported for parity with legacy article view if consumers import
void Icons;

/**
 * Lock-callout для standalone material-view (базы знаний / пространства).
 * В отличие от course-material-view, здесь нет курса в URL — back-link ведёт
 * на список знаний (либо на переданный backHref). Без редиректа на /login.
 */
function StandaloneMaterialAccessLocked({
  isAuthenticated,
  backHref,
}: {
  isAuthenticated: boolean;
  backHref: string;
}) {
  const lockReason: LockReason = isAuthenticated ? "plan_required" : "anonymous";
  const returnTo = typeof window !== "undefined" ? window.location.pathname : null;
  const ctaHref = resolveUnlockHref({ lockReason, returnTo });
  const secondaryHref = resolveSecondaryUnlockHref({ lockReason });
  return (
    <div className="flex min-h-full flex-col">
      <div className="border-b border-border/70 px-3 py-3 md:px-5">
        <Link
          href={backHref}
          className="inline-flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
        >
          <Icons.chevronLeft className="size-3.5" />К списку материалов
        </Link>
      </div>
      <div className="flex-1">
        <div className="mx-auto flex max-w-md flex-col items-stretch px-4 py-12 sm:py-16">
          <div className="rounded-2xl border border-border/60 bg-card/95 p-5 shadow-xl shadow-black/20">
            <LockCallout reason={lockReason} ctaHref={ctaHref} secondaryCtaHref={secondaryHref} />
          </div>
        </div>
      </div>
    </div>
  );
}
