"use client";

import type { SearchEducationDocumentDto, SearchHighlight } from "@/entities/search";
import { EntityTypes } from "@/shared/config/entity-types";
import { routes } from "@/shared/config/routes";
import { resolveLockCopy } from "@/shared/lib/lock-copy";
import { cn } from "@/shared/lib/css";
import { CourseBreadcrumb } from "@/shared/ui/components/course-breadcrumb";
import { CompletionBadge, ContentImage, getCompletedTitleClass } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { useEffect, useRef, useState } from "react";
import { getSearchBreadcrumbs, getSearchHref, getSearchVisual } from "../lib/search-meta";
import {
  buildSearchTitleFallbackHighlight,
  formatChapterTimestamp,
  pickChapterMatch,
  pickSearchBodyHighlights,
  pickSearchTitleHighlight,
} from "../lib/search-highlights";
import { SearchHighlightedMarkdown } from "./search-highlighted-markdown";

interface SearchItemProps {
  document: SearchEducationDocumentDto;
  highlights: SearchHighlight[];
  query?: string;
  isActive?: boolean;
  /** Material has been viewed by the current user — strikethrough title + check chip. */
  isViewed?: boolean;
  /**
   * Pass true for the first 2-3 visible results so the preview thumbnail gets
   * fetchpriority=high — palpable UX when search overlay opens.
   */
  highPriorityImage?: boolean;
  onNavigate: (href: string) => void;
  onHover?: () => void;
}

/**
 * Для локированных документов кликабельная ссылка ведёт не на контент,
 * а на courseOverview / login — это же правило использует keyboard Enter
 * в GlobalSearch.
 *
 * `startSeconds` — если совпадение по главе видео, добавляем `?t=<seconds>` чтобы
 * страница материала открыла Kinescope-плеер на нужном моменте. К locked-href'ам
 * (overview/login) не применяется.
 */
export function resolveSearchItemHref(
  document: SearchEducationDocumentDto,
  startSeconds?: number | null,
): string | null {
  const isLocked = !document.isAccessible && document.lockReason !== null;
  if (!isLocked) {
    const base = getSearchHref(document);
    if (base && typeof startSeconds === "number" && startSeconds > 0) {
      return `${base}${base.includes("?") ? "&" : "?"}t=${Math.floor(startSeconds)}`;
    }
    return base;
  }
  if (document.lockReason === "anonymous") {
    return routes.login;
  }
  return document.courseSlug ? routes.courseOverview(document.courseSlug) : null;
}

export function SearchItem({
  document,
  highlights,
  query,
  isActive,
  isViewed = false,
  highPriorityImage = false,
  onNavigate,
  onHover,
}: SearchItemProps) {
  const isLocked = !document.isAccessible && document.lockReason !== null;
  const chapterMatch = pickChapterMatch(highlights, document);
  const href = resolveSearchItemHref(document, chapterMatch?.timestampSeconds);
  const buttonRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (isActive && buttonRef.current) {
      buttonRef.current.scrollIntoView({ block: "nearest" });
    }
  }, [isActive]);
  const visual = getSearchVisual(document.entityType, document.materialKind);
  const [hasPreviewError, setHasPreviewError] = useState(false);
  const titleHighlight =
    pickSearchTitleHighlight(highlights) ??
    buildSearchTitleFallbackHighlight(document.title, query ?? "");
  // Если совпало по главе — основной body-сниппет показывает её, а description
  // прячем чтобы не дублировать визуальный шум. chapterMatch отдельно рендерим
  // с chip'ом таймкода ниже.
  const bodyHighlights = chapterMatch ? [] : pickSearchBodyHighlights(highlights);
  const breadcrumbs = getSearchBreadcrumbs(document).map((item) => ({
    label: item.label,
  }));
  // Порядок fallback'а для превью материала:
  // 1) кастомная обложка (imageId) — если автор загрузил
  // 2) Kinescope poster (videoThumbnailUrl) — для VIDEO, резолвится на бэке
  // 3) никакой (гладкий градиент-заглушка ниже)
  const previewUrl =
    document.entityType === EntityTypes.MATERIAL && !hasPreviewError
      ? document.imageId
        ? `/api/files/${document.imageId}/content`
        : document.videoThumbnailUrl || null
      : null;

  // Когда href не построился (редкий случай: locked материал без courseSlug) —
  // кнопка рисуется как disabled, клик просто не срабатывает. Никаких toast'ов,
  // чтобы не спамить пользователя на hover/keyboard Enter по disabled item'у.
  const isDisabled = href === null;

  function handleSelect() {
    if (!href) return;
    onNavigate(href);
  }

  const Icon = visual.icon;

  return (
    <button
      ref={buttonRef}
      type="button"
      role="option"
      aria-selected={isActive}
      onClick={handleSelect}
      onMouseEnter={onHover}
      aria-disabled={isDisabled}
      disabled={isDisabled}
      className={cn(
        "group w-full px-2 py-2.5 text-left transition-colors duration-150 sm:px-3",
        !isDisabled && "hover:bg-card/50",
        "focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-none",
        isLocked && "opacity-75",
        isDisabled && "cursor-not-allowed opacity-60",
        isActive && "bg-card/60",
      )}
    >
      <div className="flex items-start gap-3">
        {previewUrl ? (
          <div className="relative mt-0.5 w-24 aspect-video shrink-0 overflow-hidden rounded-md border border-border/50 bg-card/80 sm:w-28">
            <ContentImage
              src={previewUrl}
              alt={document.title}
              fill
              sizes="112px"
              className={cn(
                // rounded-md matches the wrapper: a filter:blur child escapes the parent's
                // rounded overflow-hidden clip (square corners), so self-clip it.
                "rounded-md object-cover",
                isLocked && "saturate-50 blur-[1px]",
              )}
              fetchPriority={highPriorityImage ? "high" : "auto"}
              loading={highPriorityImage ? "eager" : "lazy"}
              onError={() => setHasPreviewError(true)}
            />
            <div className="absolute inset-0 bg-gradient-to-br from-black/5 via-transparent to-black/45" />
            {document.materialKind === "VIDEO" && !isLocked && (
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="size-6 rounded-full bg-black/55 backdrop-blur-sm flex items-center justify-center">
                  <Icons.play className="size-3 text-white fill-white ml-0.5" />
                </div>
              </div>
            )}
            {isLocked && (
              <div className="absolute inset-0 flex items-center justify-center bg-black/40">
                <Icons.locked size={14} className="text-white/90" />
              </div>
            )}
          </div>
        ) : document.materialKind === "VIDEO" ? (
          // Видео без кастомной обложки — мини-тумбнейл-заглушка с play-оверлеем.
          // Kinescope thumbnail не денормализован в Typesense-индекс (был бы heavy-JOIN);
          // эта заглушка визуально отличает видео от статьи/заметки.
          <div
            className={cn(
              "relative mt-0.5 w-24 aspect-video shrink-0 overflow-hidden rounded-md border sm:w-28",
              visual.toneClassName,
            )}
          >
            <div className="absolute inset-0 bg-gradient-to-br from-teal/5 via-transparent to-teal/20" />
            <div className="absolute inset-0 flex items-center justify-center">
              {isLocked ? (
                <Icons.locked size={16} className="text-muted-foreground" />
              ) : (
                <div className="size-7 rounded-full bg-black/55 backdrop-blur-sm flex items-center justify-center">
                  <Icons.play className="size-3.5 text-white fill-white ml-0.5" />
                </div>
              )}
            </div>
          </div>
        ) : (
          <div
            className={cn(
              "mt-0.5 inline-flex size-11 shrink-0 items-center justify-center rounded-md",
              visual.toneClassName,
            )}
          >
            {isLocked ? (
              <Icons.locked size={16} className="text-muted-foreground" />
            ) : (
              <Icon size={18} className={visual.iconClassName} />
            )}
          </div>
        )}

        <div className="min-w-0 flex-1 space-y-0.5">
          {/* Breadcrumb + kind — primary nav context on top */}
          <div className="flex items-center gap-1.5 min-w-0">
            <span className="text-[9px] font-bold uppercase tracking-[0.14em] text-muted-foreground/70 shrink-0">
              {visual.label}
            </span>
            <span className="text-border shrink-0">·</span>
            <div className="min-w-0 flex-1 overflow-hidden">
              <CourseBreadcrumb items={breadcrumbs.slice(0, -1)} />
            </div>
            {isViewed && !isLocked && <CompletionBadge kind="viewed" className="shrink-0" />}
          </div>

          {titleHighlight ? (
            <SearchHighlightedMarkdown
              className={cn(
                "[&_p]:mb-0 [&_p]:text-sm [&_p]:font-semibold [&_p]:leading-[1.35] [&_p]:text-foreground",
                isViewed && "[&_p]:line-through [&_p]:text-muted-foreground/70",
              )}
            >
              {titleHighlight}
            </SearchHighlightedMarkdown>
          ) : (
            <div
              className={cn(
                "text-sm font-semibold leading-[1.35] text-foreground",
                getCompletedTitleClass(isViewed),
              )}
            >
              {document.title}
            </div>
          )}

          {bodyHighlights.length > 0 && (
            <div className="space-y-0.5 pt-0.5">
              {bodyHighlights.slice(0, 1).map((snippet, index) => (
                <SearchHighlightedMarkdown
                  key={`${document.entityType}:${document.entityId}:highlight:${index}`}
                  className="[&_p]:mb-0 [&_p]:text-[12.5px] [&_p]:leading-[1.45] [&_p]:text-muted-foreground [&_p]:line-clamp-2"
                >
                  {snippet}
                </SearchHighlightedMarkdown>
              ))}
            </div>
          )}

          {chapterMatch && (
            <div className="flex items-start gap-2 pt-0.5">
              <span
                className="inline-flex items-center gap-1 rounded bg-primary/10 px-1.5 py-0.5 text-[10.5px] font-semibold tabular-nums text-primary"
                title={`По главе видео — ${formatChapterTimestamp(chapterMatch.timestampSeconds)}`}
              >
                <Icons.play className="size-2.5 fill-current" />
                {formatChapterTimestamp(chapterMatch.timestampSeconds)}
              </span>
              <SearchHighlightedMarkdown className="[&_p]:mb-0 [&_p]:text-[12.5px] [&_p]:leading-[1.45] [&_p]:text-muted-foreground [&_p]:line-clamp-2">
                {chapterMatch.snippet}
              </SearchHighlightedMarkdown>
            </div>
          )}

          {/* Tags — demoted to chips below */}
          {document.tagTitles.length > 0 && (
            <div className="flex flex-wrap gap-1 pt-1">
              {document.tagTitles.slice(0, 3).map((tag, idx) => (
                <span
                  key={`${tag}-${idx}`}
                  className="rounded bg-muted/50 border border-border/40 px-1.5 py-0 text-[10px] leading-[1.6] text-muted-foreground"
                >
                  {tag}
                </span>
              ))}
              {document.tagTitles.length > 3 && (
                <span className="text-[10px] text-muted-foreground self-center">
                  +{document.tagTitles.length - 3}
                </span>
              )}
            </div>
          )}

          {isLocked && document.lockReason !== null && (
            <div className="flex items-center gap-1.5 pt-1 text-[11.5px] font-medium text-primary">
              {document.lockReason === "anonymous" ? (
                <Icons.login size={12} />
              ) : (
                <Icons.locked size={12} />
              )}
              <span>{resolveLockCopy(document.lockReason, document.courseTitle).cta}</span>
            </div>
          )}
        </div>
      </div>
    </button>
  );
}
